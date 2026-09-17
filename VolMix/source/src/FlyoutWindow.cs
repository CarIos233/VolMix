using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;
using System.Windows.Threading;

namespace VolMix
{
    internal enum FlyoutPage
    {
        Mixer = 0,
        Devices = 1,
        Settings = 2
    }

    /// <summary>
    /// The taskbar style flyout: output device picker, per application volume
    /// mixer and a small settings page. The window uses the Windows 11 DWM
    /// acrylic backdrop, rounded corners and the system shadow.
    /// </summary>
    internal sealed class FlyoutWindow : Window
    {
        // ------------------------------------------------------- glyphs/sizes

        private const string GlyphBack = "\uE72B";
        private const string GlyphChevron = "\uE70D";
        private const string GlyphCheck = "\uE73E";
        private const string GlyphClose = "\uE8BB";
        private const string GlyphDevice = "\uE7F4";
        private const string GlyphRefresh = "\uE72C";
        private const string GlyphSettings = "\uE713";

        private const double WindowWidth = 420.0;
        private const double CardTopPadding = 6.0;
        private const double HeaderHeight = 52.0;
        private const double FooterHeight = 44.0;
        private const double DeviceRowHeight = 44.0;
        private const double SettingRowHeight = 52.0;
        private const double ChoiceRowHeight = 36.0;
        private const double MinPageHeight = 88.0;
        private const double PageSideMargin = 8.0;
        private const double MaxPageHeightLimit = 620.0;
        private const int SparkleClicksToTrigger = 5;

        private readonly AudioEngine _engine;
        private readonly VoltTheme _theme;
        private readonly Settings _settings;
        private readonly DispatcherTimer _timer;
        private readonly DispatcherTimer _navigateGuard;

        private readonly Dictionary<string, MixerRow> _rows = new Dictionary<string, MixerRow>();
        private readonly List<MixerRow> _rowOrder = new List<MixerRow>();
        private readonly Dictionary<string, Button> _deviceButtons = new Dictionary<string, Button>();
        private readonly List<CheckBox> _appearanceRadios = new List<CheckBox>();
        private readonly List<CheckBox> _languageRadios = new List<CheckBox>();

        private Border _card;
        private Grid _headerGrid;
        private Button _deviceHeaderButton;
        private StackPanel _pageTitlePanel;
        private TextBlock _pageTitleText;
        private TextBlock _pageSubtitleText;
        private Button _backButton;
        private Button _sparkleButton;
        private TextBlock _deviceNameText;
        private Canvas _overlay;
        private Grid _contentHost;

        private int _sparkleClicks;

        private Grid _pageHost;
        private Grid _mixerPage;
        private Grid _devicePage;
        private Grid _settingsPage;

        private ScrollViewer _mixerScroll;
        private StackPanel _rowsHost;
        private TextBlock _emptyText;
        private Grid _masterHost;
        private MixerRow _masterRow;
        private Border _masterDivider;

        private ScrollViewer _deviceScroll;
        private StackPanel _deviceHost;

        private ScrollViewer _settingsScroll;
        private StackPanel _settingsHost;
        private CheckBox _followSystemToggle;
        private CheckBox _startupToggle;
        private CheckBox _startMenuToggle;
        private TextBlock _appearanceHint;

        private Grid _footerGrid;

        private string _deviceId;
        private string _deviceName;
        private List<AudioDevice> _devices = new List<AudioDevice>();
        private FlyoutPage _currentPage = FlyoutPage.Mixer;
        private bool _animating;
        private bool _themeDirty;
        private bool _acrylicActive;
        private double _maxPageHeight = 480.0;
        private double _maxPageOverride;
        private double _settingsContentHeight;

        public event EventHandler ExitRequested;
        public event EventHandler DeviceMuteChanged;

        public FlyoutWindow(AudioEngine engine, VoltTheme theme, Settings settings)
        {
            _engine = engine;
            _theme = theme;
            _settings = settings;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;   // the backdrop needs a normal (non layered) window
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.Manual;
            WindowStartupLocation = WindowStartupLocation.Manual;
            Width = WindowWidth;
            Height = 480;
            FontFamily = InterfaceFont.DefaultFamily;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(this, TextFormattingMode.Ideal);

            // keeps a real window frame, so DWM still draws the shadow and the round corners
            var chrome = new WindowChrome();
            chrome.CornerRadius = new CornerRadius(0);
            chrome.GlassFrameThickness = new Thickness(1);
            chrome.CaptionHeight = 0;
            chrome.ResizeBorderThickness = new Thickness(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, chrome);

            BuildUi();

            // give the window a sensible position right away (before any Show call)
            PositionHelper.PlaceAboveCursor(this, PositionHelper.HalfCentimetre);

            _timer = new DispatcherTimer(DispatcherPriority.Background);
            _timer.Interval = TimeSpan.FromMilliseconds(600);
            _timer.Tick += delegate { Refresh(); };

            _navigateGuard = new DispatcherTimer(DispatcherPriority.Background);
            _navigateGuard.Interval = TimeSpan.FromMilliseconds(280);
            _navigateGuard.Tick += delegate
            {
                _navigateGuard.Stop();
                _animating = false;
            };

            SourceInitialized += OnSourceInitialized;
            Deactivated += OnDeactivated;
            PreviewKeyDown += OnPreviewKeyDown;
            PreviewMouseWheel += OnWindowWheel;
            MouseEnter += delegate
            {
                if (!IsActive)
                {
                    try
                    {
                        NativeMethods.ForceForeground(WindowMaterial.HandleOf(this));
                    }
                    catch
                    {
                    }
                }
            };
            PreviewMouseDown += delegate
            {
                // a click inside the panel is a user gesture: make sure we own the focus again
                try
                {
                    Activate();
                }
                catch
                {
                }
            };
            IsVisibleChanged += delegate { if (!IsVisible) _timer.Stop(); };
        }

        // ------------------------------------------------------------ acrylic

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            ApplyBackdrop();

            // WPF ignores mouse wheel messages while its window is not active, so the
            // wheel is handled on the raw window message instead.
            var source = PresentationSource.FromVisual(this) as HwndSource;
            if (source != null)
            {
                source.AddHook(WindowMessageHook);
            }

            InstallWheelHook();
        }

        private IntPtr _wheelHook = IntPtr.Zero;
        private NativeMethods.LowLevelMouseProc _wheelHookProc;

        /// <summary>
        /// Last resort for the mouse wheel: a low level hook. It only reacts while
        /// the pointer is inside the panel, so the wheel always scrolls the panel
        /// even when the window could not take the foreground.
        /// </summary>
        private void InstallWheelHook()
        {
            if (_wheelHook != IntPtr.Zero)
            {
                return;
            }
            try
            {
                _wheelHookProc = WheelHookCallback;
                _wheelHook = NativeMethods.SetWindowsHookEx(NativeMethods.WH_MOUSE_LL,
                    _wheelHookProc, IntPtr.Zero, 0);
            }
            catch
            {
                _wheelHook = IntPtr.Zero;
            }
            DiagLog.Write("wheel hook: " + (_wheelHook != IntPtr.Zero ? "installed" : "failed"));
        }

        public void ReleaseHooks()
        {
            if (_wheelHook != IntPtr.Zero)
            {
                try
                {
                    NativeMethods.UnhookWindowsHookEx(_wheelHook);
                }
                catch
                {
                }
                _wheelHook = IntPtr.Zero;
            }
        }

        private IntPtr WheelHookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && IsVisible && wParam.ToInt32() == (int)NativeMethods.WM_MOUSEWHEEL)
                {
                    var info = (MSLLHOOKSTRUCT)Marshal.PtrToStructure(lParam,
                        typeof(MSLLHOOKSTRUCT));
                    if (IsPointInsidePanel(info.pt))
                    {
                        int delta = (short)((info.mouseData >> 16) & 0xFFFF);
                        DiagLog.Write("wheel hook: delta=" + delta + " at " + info.pt.X + "," + info.pt.Y
                            + " offset=" + Math.Round(DebugScrollOffset, 1)
                            + " scrollable=" + Math.Round(DebugScrollableHeight, 1));
                        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control
                            && ScrollActivePage(delta / 120.0 * 56.0))
                        {
                            return new IntPtr(1);
                        }
                    }
                }
            }
            catch
            {
            }
            return NativeMethods.CallNextHookEx(_wheelHook, nCode, wParam, lParam);
        }

        private bool IsPointInsidePanel(POINT point)
        {
            IntPtr hwnd = WindowMaterial.HandleOf(this);
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }
            RECT rect;
            if (!NativeMethods.GetWindowRect(hwnd, out rect))
            {
                return false;
            }
            return point.X >= rect.Left && point.X < rect.Right
                && point.Y >= rect.Top && point.Y < rect.Bottom;
        }

        private IntPtr WindowMessageHook(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == (int)NativeMethods.WM_MOUSEWHEEL)
            {
                long raw = wParam.ToInt64();
                int delta = (short)((raw >> 16) & 0xFFFF);
                if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
                {
                    DiagLog.Write("wheel message: delta=" + delta
                        + " page=" + _currentPage
                        + " scrollable=" + Math.Round(DebugScrollableHeight, 1)
                        + " offset=" + Math.Round(DebugScrollOffset, 1));
                    if (ScrollActivePage(delta / 120.0 * 56.0))
                    {
                        handled = true;
                    }
                }
            }
            return IntPtr.Zero;
        }

        /// <summary>Enables the Windows 11 acrylic backdrop, round corners and dark frame.</summary>
        private void ApplyBackdrop()
        {
            _acrylicActive = WindowMaterial.Apply(this, _theme, 1.0);
            UpdateCardSurface();
        }

        /// <summary>Re-applies the acrylic tint at a fraction of its opacity.</summary>
        private void ApplyMaterialStrength(double strength)
        {
            LastMaterialStrength = strength;
            _acrylicActive = WindowMaterial.Apply(this, _theme, strength);
        }

        public double LastMaterialStrength { get; private set; }

        /// <summary>Diagnostics: keep the panel visible even when it loses focus.</summary>
        public bool SuppressAutoHide { get; set; }

        /// <summary>Diagnostics: page scroll offset and scrollable range.</summary>
        public double DebugScrollOffset
        {
            get { return _settingsScroll != null ? _settingsScroll.VerticalOffset : 0.0; }
        }

        public double DebugScrollableHeight
        {
            get { return _settingsScroll != null ? _settingsScroll.ScrollableHeight : 0.0; }
        }

        /// <summary>Diagnostics: the last acrylic gradient colour that was applied.</summary>
        public int LastGradient { get { return WindowMaterial.LastGradient; } }
        private void UpdateCardSurface()
        {
            if (_card != null)
            {
                _card.Background = _acrylicActive ? (Brush)_theme.Card : (Brush)_theme.CardSolid;
            }
        }

        public bool AcrylicActive
        {
            get { return _acrylicActive; }
        }

        // --------------------------------------------------------------- layout

        private void BuildUi()
        {
            _settingsContentHeight = 0.0;
            Resources[typeof(ToolTip)] = UiKit.ToolTipStyle(_theme);

            _card = new Border();
            _card.CornerRadius = new CornerRadius(8);
            _card.BorderBrush = _theme.CardBorder;
            _card.BorderThickness = new Thickness(1);
            UpdateCardSurface();
            Content = _card;

            _contentHost = new Grid();
            _contentHost.RenderTransform = new TranslateTransform();
            _card.Child = _contentHost;

            var layout = new Grid();
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            _contentHost.Children.Add(layout);

            BuildHeader();
            BuildFooter();
            BuildPages();

            layout.Children.Add(_headerGrid);
            layout.Children.Add(_pageHost);
            layout.Children.Add(_footerGrid);
            Grid.SetRow(_headerGrid, 0);
            Grid.SetRow(_pageHost, 1);
            Grid.SetRow(_footerGrid, 2);

            _overlay = new Canvas();
            _overlay.Background = null;
            _overlay.IsHitTestVisible = false;
            _overlay.ClipToBounds = true;
            _overlay.Opacity = 1;
            Grid.SetRowSpan(_overlay, 3);
            Panel.SetZIndex(_overlay, 50);
            layout.Children.Add(_overlay);
        }

        private void BuildHeader()
        {
            _headerGrid = new Grid();
            _headerGrid.Height = HeaderHeight;
            _headerGrid.Margin = new Thickness(8, CardTopPadding, 8, 0);
            _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _backButton = IconButton(GlyphBack, 32, L.T("tooltip.back"));
            _backButton.Visibility = Visibility.Collapsed;
            _backButton.Margin = new Thickness(0, 0, 4, 0);
            _backButton.Click += delegate { NavigateTo(FlyoutPage.Mixer); };
            Grid.SetColumn(_backButton, 0);
            _headerGrid.Children.Add(_backButton);

            _deviceHeaderButton = new Button();
            _deviceHeaderButton.Template = UiKit.RowButtonTemplate(_theme);
            _deviceHeaderButton.Cursor = Cursors.Hand;
            _deviceHeaderButton.ToolTip = L.T("tooltip.devices");
            _deviceHeaderButton.Click += delegate { NavigateTo(FlyoutPage.Devices); };

            var deviceContent = new Grid();
            deviceContent.Margin = new Thickness(6, 0, 4, 0);
            deviceContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            deviceContent.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            deviceContent.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock deviceGlyph = UiKit.Text(GlyphDevice, _theme.Text, 16, FontWeights.Normal, true);
            deviceGlyph.Width = 26;
            deviceGlyph.TextAlignment = TextAlignment.Center;
            deviceGlyph.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(deviceGlyph, 0);
            deviceContent.Children.Add(deviceGlyph);

            var deviceTexts = new StackPanel();
            deviceTexts.VerticalAlignment = VerticalAlignment.Center;
            deviceTexts.Margin = new Thickness(8, 0, 8, 0);
            TextBlock caption = UiKit.Text(L.T("device.caption"), _theme.TextSecondary, 11, FontWeights.Normal, false);
            caption.Margin = new Thickness(0, 0, 0, 1);
            _deviceNameText = UiKit.Text(L.T("device.detecting"), _theme.Text, InterfaceFont.Body, FontWeights.SemiBold, false);
            deviceTexts.Children.Add(caption);
            deviceTexts.Children.Add(_deviceNameText);
            Grid.SetColumn(deviceTexts, 1);
            deviceContent.Children.Add(deviceTexts);

            TextBlock chevron = UiKit.Text(GlyphChevron, _theme.TextSecondary, 12, FontWeights.Normal, true);
            chevron.HorizontalAlignment = HorizontalAlignment.Right;
            chevron.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(chevron, 2);
            deviceContent.Children.Add(chevron);

            _deviceHeaderButton.Content = deviceContent;
            Grid.SetColumn(_deviceHeaderButton, 1);
            _headerGrid.Children.Add(_deviceHeaderButton);

            _pageTitlePanel = new StackPanel();
            _pageTitlePanel.VerticalAlignment = VerticalAlignment.Center;
            _pageTitlePanel.Margin = new Thickness(4, 0, 4, 0);
            _pageTitlePanel.Visibility = Visibility.Collapsed;
            _pageTitleText = UiKit.Text("", _theme.Text, 18, FontWeights.SemiBold, false);
            _pageSubtitleText = UiKit.Text("", _theme.TextSecondary, 11, FontWeights.Normal, false);
            _pageSubtitleText.Visibility = Visibility.Collapsed;
            _pageTitlePanel.Children.Add(_pageTitleText);
            _pageTitlePanel.Children.Add(_pageSubtitleText);
            Grid.SetColumn(_pageTitlePanel, 1);
            _headerGrid.Children.Add(_pageTitlePanel);

            _sparkleButton = new Button();
            _sparkleButton.Width = 30;
            _sparkleButton.Height = 30;
            _sparkleButton.Template = UiKit.IconButtonTemplate(_theme);
            _sparkleButton.Cursor = Cursors.Hand;
            _sparkleButton.Content = FluentKit.CreateSparkle(20);
            _sparkleButton.VerticalAlignment = VerticalAlignment.Center;
            _sparkleButton.Visibility = Visibility.Collapsed;
            _sparkleButton.Click += OnSparkleClick;
            Grid.SetColumn(_sparkleButton, 2);
            _headerGrid.Children.Add(_sparkleButton);
        }

        private void BuildFooter()
        {
            _footerGrid = new Grid();
            _footerGrid.Height = FooterHeight;
            _footerGrid.Margin = new Thickness(8, 0, 8, 6);
            _footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _footerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock brand = UiKit.Text("VolMix " + Settings.VersionText, _theme.TextTertiary,
                InterfaceFont.Caption, FontWeights.Normal, false);
            brand.Margin = new Thickness(8, 0, 0, 0);
            Grid.SetColumn(brand, 0);
            _footerGrid.Children.Add(brand);

            var buttons = new StackPanel();
            buttons.Orientation = Orientation.Horizontal;
            buttons.HorizontalAlignment = HorizontalAlignment.Right;
            buttons.VerticalAlignment = VerticalAlignment.Center;

            Button refresh = IconButton(GlyphRefresh, 32, L.T("tooltip.refresh"));
            refresh.Click += delegate { Refresh(); };
            buttons.Children.Add(refresh);

            Button settings = IconButton(GlyphSettings, 32, L.T("tooltip.settings"));
            settings.Click += delegate { NavigateTo(FlyoutPage.Settings); };
            buttons.Children.Add(settings);

            Button close = IconButton(GlyphClose, 32, L.T("tooltip.close"));
            close.Click += delegate { HideFlyout(); };
            buttons.Children.Add(close);

            Grid.SetColumn(buttons, 1);
            _footerGrid.Children.Add(buttons);
        }

        // ---------------------------------------------------------------- pages

        private void BuildPages()
        {
            _pageHost = new Grid();
            _pageHost.Height = MinPageHeight;
            _pageHost.Margin = new Thickness(PageSideMargin, 0, PageSideMargin, 0);

            _mixerPage = BuildMixerPage();
            _devicePage = BuildDevicePage();
            _settingsPage = BuildSettingsPage();

            _devicePage.Visibility = Visibility.Collapsed;
            _settingsPage.Visibility = Visibility.Collapsed;

            _pageHost.Children.Add(_mixerPage);
            _pageHost.Children.Add(_devicePage);
            _pageHost.Children.Add(_settingsPage);
        }

        private Grid BuildMixerPage()
        {
            var page = new Grid();
            page.RenderTransform = new TranslateTransform();
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            page.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            _masterRow = new MixerRow(_theme, CreateMasterModel(), OnVolumeChanged, OnMuteChanged);
            _masterHost = new Grid();
            _masterHost.Margin = new Thickness(2, 0, 2, 0);
            _masterHost.Children.Add(_masterRow.Root);
            Grid.SetRow(_masterHost, 0);
            page.Children.Add(_masterHost);

            _masterDivider = new Border();
            _masterDivider.Height = 1;
            _masterDivider.Background = _theme.Divider;
            _masterDivider.Margin = new Thickness(6, 4, 6, 4);
            Grid.SetRow(_masterDivider, 1);
            page.Children.Add(_masterDivider);

            _mixerScroll = new ScrollViewer();
            _mixerScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
            _mixerScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _mixerScroll.Resources.Add(typeof(ScrollBar), UiKit.ScrollBarStyle(_theme));
            _mixerScroll.Height = MinPageHeight;
            HookWheel(_mixerScroll);

            _rowsHost = new StackPanel();
            _rowsHost.Margin = new Thickness(2, 0, 2, 0);
            _mixerScroll.Content = _rowsHost;

            _emptyText = UiKit.Text(L.T("mixer.empty"), _theme.TextSecondary,
                InterfaceFont.Caption, FontWeights.Normal, false);
            _emptyText.HorizontalAlignment = HorizontalAlignment.Center;
            _emptyText.Margin = new Thickness(0, 26, 0, 26);
            _emptyText.Visibility = Visibility.Collapsed;
            _rowsHost.Children.Add(_emptyText);

            Grid.SetRow(_mixerScroll, 2);
            page.Children.Add(_mixerScroll);
            return page;
        }

        private Grid BuildDevicePage()
        {
            var page = new Grid();
            page.RenderTransform = new TranslateTransform();

            _deviceScroll = new ScrollViewer();
            _deviceScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
            _deviceScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _deviceScroll.Resources.Add(typeof(ScrollBar), UiKit.ScrollBarStyle(_theme));
            HookWheel(_deviceScroll);

            _deviceHost = new StackPanel();
            _deviceHost.Margin = new Thickness(0, 6, 0, 6);
            _deviceScroll.Content = _deviceHost;
            page.Children.Add(_deviceScroll);
            return page;
        }

        private Grid BuildSettingsPage()
        {
            var page = new Grid();
            page.RenderTransform = new TranslateTransform();

            _settingsScroll = new ScrollViewer();
            _settingsScroll.VerticalScrollBarVisibility = ScrollBarVisibility.Visible;
            _settingsScroll.HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
            _settingsScroll.Resources.Add(typeof(ScrollBar), UiKit.ScrollBarStyle(_theme));
            HookWheel(_settingsScroll);

            _settingsHost = new StackPanel();
            _settingsHost.Margin = new Thickness(0, 6, 0, 6);
            _settingsScroll.Content = _settingsHost;
            page.Children.Add(_settingsScroll);

            AddToggleRow(L.T("set.hideInactive"), L.T("set.hideInactive.desc"),
                _settings.HideInactive, delegate(bool value)
                {
                    _settings.HideInactive = value;
                    _settings.Save();
                });

            AddToggleRow(L.T("set.systemSounds"), L.T("set.systemSounds.desc"),
                _settings.ShowSystemSounds, delegate(bool value)
                {
                    _settings.ShowSystemSounds = value;
                    _settings.Save();
                });

            AddToggleRow(L.T("set.masterVolume"), L.T("set.masterVolume.desc"),
                _settings.ShowMasterVolume, delegate(bool value)
                {
                    _settings.ShowMasterVolume = value;
                    _settings.Save();
                });

            _followSystemToggle = AddToggleRow(L.T("set.followSystem"), L.T("set.followSystem.desc"),
                _settings.Appearance == AppearanceMode.FollowSystem, delegate(bool value)
                {
                    _settings.Appearance = value
                        ? AppearanceMode.FollowSystem
                        : (_theme.Dark ? AppearanceMode.Dark : AppearanceMode.Light);
                    _settings.Save();
                    RefreshAppearanceRows();
                    _settings.RaiseChanged();
                });

            AddAppearanceBlock();
            AddLanguageBlock();

            _startupToggle = AddToggleRow(L.T("set.startup"), L.T("set.startup.desc"),
                Settings.GetStartWithWindows(), delegate(bool value)
                {
                    if (!Settings.SetStartWithWindows(value) && _startupToggle != null)
                    {
                        _startupToggle.IsChecked = !value;
                        UiKit.ApplyToggle(_startupToggle, _theme, !value, true);
                    }
                });

            _startMenuToggle = AddToggleRow(L.T("set.startMenu"), L.T("set.startMenu.desc"),
                StartMenuShortcut.Exists(), delegate(bool value)
                {
                    if (!StartMenuShortcut.Set(value) && _startMenuToggle != null)
                    {
                        _startMenuToggle.IsChecked = !value;
                        UiKit.ApplyToggle(_startMenuToggle, _theme, !value, true);
                    }
                });

            AddDivider(_settingsHost);
            AddActionSetting(L.T("set.exit"), L.T("set.exit.desc"), false,
                delegate { RaiseExitRequested(); });
            AddDivider(_settingsHost);
            AddAbout();

            return page;
        }

        // ------------------------------------------------------- settings rows

        private void OnWindowWheel(object sender, MouseWheelEventArgs e)
        {
            DiagLog.Write("wheel routed: page=" + _currentPage + " delta=" + e.Delta
                + " scrollable=" + Math.Round(DebugScrollableHeight, 1));
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                return;   // Ctrl + wheel belongs to the mixer rows
            }
            ScrollViewer target = ActiveScroll();
            if (target == null || target.ScrollableHeight <= 0.0)
            {
                return;
            }
            e.Handled = ScrollActivePage((e.Delta / 120.0) * 56.0);
        }

        /// <summary>Scrolls the page shown right now. Returns false when there is nothing to scroll.</summary>
        public bool ScrollActivePage(double pixels)
        {
            ScrollViewer target = ActiveScroll();
            if (target == null || target.ScrollableHeight <= 0.0)
            {
                return false;
            }
            double offset = target.VerticalOffset - pixels;
            if (offset < 0.0)
            {
                offset = 0.0;
            }
            if (offset > target.ScrollableHeight)
            {
                offset = target.ScrollableHeight;
            }
            target.ScrollToVerticalOffset(offset);
            target.UpdateLayout();
            return true;
        }

        private ScrollViewer ActiveScroll()
        {
            if (_currentPage == FlyoutPage.Settings)
            {
                return _settingsScroll;
            }
            if (_currentPage == FlyoutPage.Devices)
            {
                return _deviceScroll;
            }
            return _mixerScroll;
        }

        private void HookWheel(ScrollViewer scroll)
        {
            scroll.PreviewMouseWheel += delegate(object sender, MouseWheelEventArgs e)
            {
                // rows use Ctrl + wheel for volume, let those through
                if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                {
                    return;
                }
                if (scroll.ScrollableHeight <= 0)
                {
                    return;
                }
                e.Handled = true;
                double step = (e.Delta / 120.0) * 56.0;
                double target = scroll.VerticalOffset - step;
                if (target < 0)
                {
                    target = 0;
                }
                if (target > scroll.ScrollableHeight)
                {
                    target = scroll.ScrollableHeight;
                }
                scroll.ScrollToVerticalOffset(target);
            };
        }

        private CheckBox AddToggleRow(string title, string description, bool value, Action<bool> handler)
        {
            var row = new Grid();
            row.Height = SettingRowHeight;
            row.Background = Brushes.Transparent;
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var texts = new StackPanel();
            texts.VerticalAlignment = VerticalAlignment.Center;
            texts.Margin = new Thickness(10, 0, 10, 0);
            texts.Children.Add(UiKit.Text(title, _theme.Text, InterfaceFont.Body, FontWeights.Normal, false));
            texts.Children.Add(UiKit.Text(description, _theme.TextSecondary, InterfaceFont.Caption, FontWeights.Normal, false));
            Grid.SetColumn(texts, 0);
            row.Children.Add(texts);

            var toggle = new CheckBox();
            toggle.IsChecked = value;
            toggle.Template = UiKit.ToggleTemplate(_theme);
            toggle.Width = 40;
            toggle.Height = 20;
            toggle.VerticalAlignment = VerticalAlignment.Center;
            toggle.Margin = new Thickness(0, 0, 10, 0);
            toggle.Cursor = Cursors.Hand;
            UiKit.InitToggle(toggle, _theme, value);
            toggle.Click += delegate
            {
                UiKit.ApplyToggle(toggle, _theme, toggle.IsChecked == true, true);
                if (handler != null)
                {
                    handler(toggle.IsChecked == true);
                }
            };
            Grid.SetColumn(toggle, 1);
            row.Children.Add(toggle);

            row.MouseLeftButtonUp += delegate
            {
                toggle.IsChecked = !(toggle.IsChecked == true);
                UiKit.ApplyToggle(toggle, _theme, toggle.IsChecked == true, true);
                if (handler != null)
                {
                    handler(toggle.IsChecked == true);
                }
            };

            _settingsHost.Children.Add(WrapHover(row));
            return toggle;
        }

        private void AddAppearanceBlock()
        {
            var panel = new StackPanel();
            panel.Margin = new Thickness(10, 6, 10, 8);

            panel.Children.Add(UiKit.Text(L.T("set.appearance"), _theme.Text, InterfaceFont.Body, FontWeights.Normal, false));
            _appearanceHint = UiKit.Text("", _theme.TextSecondary, InterfaceFont.Caption, FontWeights.Normal, false);
            _appearanceHint.Margin = new Thickness(0, 1, 0, 4);
            panel.Children.Add(_appearanceHint);

            _appearanceRadios.Clear();
            var options = new StackPanel();
            options.Orientation = Orientation.Horizontal;
            options.Margin = new Thickness(-8, 0, 0, 0);
            options.Children.Add(RadioRow(L.T("theme.light.label"), AppearanceMode.Light, 92));
            options.Children.Add(RadioRow(L.T("theme.dark.label"), AppearanceMode.Dark, 92));
            panel.Children.Add(options);

            _settingsHost.Children.Add(panel);
            RefreshAppearanceRows();
        }

        private void AddLanguageBlock()
        {
            var panel = new StackPanel();
            panel.Margin = new Thickness(10, 6, 10, 8);

            panel.Children.Add(UiKit.Text(L.T("set.language"), _theme.Text, InterfaceFont.Body, FontWeights.Normal, false));
            TextBlock hint = UiKit.Text(L.T("set.language.desc"), _theme.TextSecondary,
                InterfaceFont.Caption, FontWeights.Normal, false);
            hint.Margin = new Thickness(0, 1, 0, 4);
            panel.Children.Add(hint);

            _languageRadios.Clear();
            panel.Children.Add(LanguageRow(L.T("language.auto"), AppLanguage.Auto));
            panel.Children.Add(LanguageRow(L.T("language.zh"), AppLanguage.Chinese));
            panel.Children.Add(LanguageRow(L.T("language.en"), AppLanguage.English));

            _settingsHost.Children.Add(panel);
            RefreshLanguageRows();
        }

        private Grid LanguageRow(string text, AppLanguage language)
        {
            var row = new Grid();
            row.Height = ChoiceRowHeight;
            row.Background = Brushes.Transparent;
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var box = new CheckBox();
            box.Template = FluentKit.RadioTemplate(_theme);
            box.Width = 20;
            box.Height = 20;
            box.Margin = new Thickness(2, 0, 12, 0);
            box.VerticalAlignment = VerticalAlignment.Center;
            box.Cursor = Cursors.Hand;
            box.Tag = language;
            box.IsChecked = _settings.Language == language;
            FluentKit.InitRadio(box, _theme, box.IsChecked == true);
            box.Click += delegate { SelectLanguage(language); };
            Grid.SetColumn(box, 0);
            row.Children.Add(box);

            TextBlock label = UiKit.Text(text, _theme.Text, InterfaceFont.Body, FontWeights.Normal, false);
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            row.Children.Add(label);

            row.MouseLeftButtonUp += delegate
            {
                if (box.IsMouseOver)
                {
                    return;
                }
                SelectLanguage(language);
            };

            _languageRadios.Add(box);
            return WrapHover(row);
        }

        private void SelectLanguage(AppLanguage language)
        {
            if (_settings.Language == language)
            {
                return;
            }
            _settings.Language = language;
            _settings.Save();
            L.Apply(language);
            RefreshLanguageRows();
            _settings.RaiseChanged();
        }

        private void RefreshLanguageRows()
        {
            for (int i = 0; i < _languageRadios.Count; i++)
            {
                CheckBox box = _languageRadios[i];
                var language = (AppLanguage)box.Tag;
                bool on = _settings.Language == language;
                box.IsChecked = on;
                FluentKit.ApplyRadio(box, _theme, on, false);
            }
        }

        private Grid RadioRow(string text, AppearanceMode mode, double width)
        {
            var row = new Grid();
            row.Height = ChoiceRowHeight;
            if (width > 0)
            {
                row.Width = width;
            }
            row.Background = Brushes.Transparent;
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var box = new CheckBox();
            box.Template = FluentKit.RadioTemplate(_theme);
            box.Width = 20;
            box.Height = 20;
            box.Margin = new Thickness(2, 0, 12, 0);
            box.VerticalAlignment = VerticalAlignment.Center;
            box.Cursor = Cursors.Hand;
            box.Tag = mode;
            box.IsChecked = _settings.Appearance == mode;
            FluentKit.InitRadio(box, _theme, box.IsChecked == true);
            box.Click += delegate { SelectAppearance(mode); };
            Grid.SetColumn(box, 0);
            row.Children.Add(box);

            TextBlock label = UiKit.Text(text, _theme.Text, InterfaceFont.Body, FontWeights.Normal, false);
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            row.Children.Add(label);

            row.MouseLeftButtonUp += delegate
            {
                if (box.IsMouseOver)
                {
                    return;
                }
                SelectAppearance(mode);
            };

            _appearanceRadios.Add(box);
            return WrapHover(row);
        }

        private void SelectAppearance(AppearanceMode mode)
        {
            _settings.Appearance = mode;
            _settings.Save();
            RefreshAppearanceRows();
            _settings.RaiseChanged();
        }

        private void RefreshAppearanceRows()
        {
            bool following = _settings.Appearance == AppearanceMode.FollowSystem;
            bool effectiveDark = _settings.ResolveDark(VoltThemeFactory.IsSystemDark());

            for (int i = 0; i < _appearanceRadios.Count; i++)
            {
                CheckBox box = _appearanceRadios[i];
                var mode = (AppearanceMode)box.Tag;
                bool on = following
                    ? ((mode == AppearanceMode.Dark) == effectiveDark)
                    : (_settings.Appearance == mode);
                box.IsEnabled = !following;
                box.IsChecked = on;
                FluentKit.ApplyRadio(box, _theme, on, false);
            }

            if (_appearanceHint != null)
            {
                if (following)
                {
                    _appearanceHint.Text = L.T("set.appearance.following", effectiveDark ? L.T("theme.dark") : L.T("theme.light"));
                }
                else
                {
                    _appearanceHint.Text = L.T("set.appearance.fixed", _settings.Appearance == AppearanceMode.Dark ? L.T("theme.dark") : L.T("theme.light"));
                }
            }
        }

        private void AddActionSetting(string title, string description, bool danger, Action handler)
        {
            var row = new Grid();
            row.Height = SettingRowHeight;
            row.Background = Brushes.Transparent;

            var texts = new StackPanel();
            texts.VerticalAlignment = VerticalAlignment.Center;
            texts.Margin = new Thickness(10, 0, 10, 0);
            texts.Children.Add(UiKit.Text(title, danger ? _theme.DangerText : _theme.Text,
                InterfaceFont.Body, FontWeights.Normal, false));
            texts.Children.Add(UiKit.Text(description, _theme.TextSecondary,
                InterfaceFont.Caption, FontWeights.Normal, false));
            row.Children.Add(texts);

            row.Cursor = Cursors.Hand;
            row.MouseLeftButtonUp += delegate
            {
                if (handler != null)
                {
                    handler();
                }
            };
            _settingsHost.Children.Add(WrapHover(row));
        }

        private void AddAbout()
        {
            var panel = new StackPanel();
            panel.Margin = new Thickness(10, 10, 10, 12);
            panel.Children.Add(UiKit.Text("VolMix " + Settings.VersionText, _theme.Text,
                InterfaceFont.Body, FontWeights.SemiBold, false));

            TextBlock line1 = UiKit.Text(L.T("about.tagline"),
                _theme.TextSecondary, InterfaceFont.Caption, FontWeights.Normal, false);
            TextBlock line2 = UiKit.Text("© 2026 Carlise · github.com/CarIos233",
                _theme.Accent, InterfaceFont.Caption, FontWeights.Normal, false);

            line1.Margin = new Thickness(0, 2, 0, 0);
            line2.Margin = new Thickness(0, 2, 0, 0);


            line2.Cursor = Cursors.Hand;
            line2.ToolTip = L.T("about.link");
            line2.MouseLeftButtonUp += delegate
            {
                try
                {
                    System.Diagnostics.Process.Start("https://github.com/CarIos233");
                }
                catch
                {
                }
            };

            panel.Children.Add(line1);
            panel.Children.Add(line2);
            _settingsHost.Children.Add(panel);
        }

        private void AddDivider(StackPanel host)
        {
            var divider = new Border();
            divider.Height = 1;
            divider.Background = _theme.Divider;
            divider.Margin = new Thickness(6, 2, 6, 2);
            host.Children.Add(divider);
        }

        private Grid WrapHover(Grid row)
        {
            var host = new Grid();
            var hover = new Border();
            hover.CornerRadius = new CornerRadius(4);
            hover.Background = _theme.SubtleFill;
            hover.Opacity = 0;
            host.Children.Add(hover);
            host.Children.Add(row);
            host.MouseEnter += delegate { UiKit.Animate(hover, UIElement.OpacityProperty, 1, 120, UiKit.FluentDecelerate()); };
            host.MouseLeave += delegate { UiKit.Animate(hover, UIElement.OpacityProperty, 0, 150, UiKit.FluentStandard()); };
            return host;
        }

        private Button IconButton(string glyph, double size, string tooltip)
        {
            var button = new Button();
            button.Width = size;
            button.Height = size;
            button.Template = UiKit.IconButtonTemplate(_theme);
            button.Cursor = Cursors.Hand;
            button.ToolTip = tooltip;
            button.Content = UiKit.Text(glyph, _theme.Text, 14, FontWeights.Normal, true);
            button.VerticalAlignment = VerticalAlignment.Center;
            return button;
        }

        // -------------------------------------------------------------- wiring

        private AudioSessionModel CreateMasterModel()
        {
            var model = new AudioSessionModel();
            model.Key = "master";
            model.ProcessId = 0;
            model.IsSystemSounds = true;
            model.Name = L.T("mixer.master");
            model.Volume = 0;
            model.IsMuted = false;
            return model;
        }

        private void OnVolumeChanged(MixerRow row, double value)
        {
            if (row == _masterRow)
            {
                _engine.SetDeviceVolume(_deviceId, (float)(value / 100.0));
                return;
            }
            if (row.Model != null)
            {
                _engine.SetSessionVolume(row.Model, (float)(value / 100.0));
            }
        }

        private void OnMuteChanged(MixerRow row, bool muted)
        {
            if (row == _masterRow)
            {
                _engine.SetDeviceMute(_deviceId, muted);
                if (row.Model != null)
                {
                    row.Model.IsMuted = muted;
                    row.Apply(row.Model);
                }
                RaiseDeviceMuteChanged();
                return;
            }
            if (row.Model != null)
            {
                _engine.SetSessionMute(row.Model, muted);
                row.Model.IsMuted = muted;
                row.Apply(row.Model);
            }
        }

        private void OnDeactivated(object sender, EventArgs e)
        {
            if (SuppressAutoHide)
            {
                return;
            }
            if (_animating || _closing)
            {
                return;
            }
            if (IsDraggingAnyRow())
            {
                return;
            }
            HideFlyout();
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape)
            {
                e.Handled = true;
                if (_currentPage != FlyoutPage.Mixer)
                {
                    NavigateTo(FlyoutPage.Mixer);
                }
                else
                {
                    HideFlyout();
                }
                return;
            }

            // keyboard scrolling, so the panel can always be navigated without a wheel
            double step = 0;
            if (e.Key == Key.Down)
            {
                step = 56;
            }
            else if (e.Key == Key.Up)
            {
                step = -56;
            }
            else if (e.Key == Key.PageDown)
            {
                step = 200;
            }
            else if (e.Key == Key.PageUp)
            {
                step = -200;
            }
            else if (e.Key == Key.End)
            {
                step = 100000;
            }
            else if (e.Key == Key.Home)
            {
                step = -100000;
            }

            if (step != 0 && ScrollActivePage(step))
            {
                e.Handled = true;
            }
        }

        private bool IsDraggingAnyRow()
        {
            if (_masterRow != null && _masterRow.Dragging)
            {
                return true;
            }
            foreach (KeyValuePair<string, MixerRow> pair in _rows)
            {
                if (pair.Value.Dragging)
                {
                    return true;
                }
            }
            return false;
        }

        private void RaiseExitRequested()
        {
            EventHandler handler = ExitRequested;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        private void RaiseDeviceMuteChanged()
        {
            EventHandler handler = DeviceMuteChanged;
            if (handler != null)
            {
                handler(this, EventArgs.Empty);
            }
        }

        // ----------------------------------------------------------- lifecycle


        private bool _closing;

        public void ToggleFlyout()
        {
            if (IsVisible)
            {
                HideFlyout();
            }
            else
            {
                ShowFlyout(FlyoutPage.Mixer);
            }
        }

        public void ShowFlyout(FlyoutPage page)
        {
            _closing = false;
            _themeDirty = true;
            SetPage(page, true);
            UpdateScreenBudget();
            Refresh();

            // position first, then show: otherwise the very first frame is painted at
            // the default window position (top left) and flashes on cold start
            PositionHelper.PlaceAboveCursor(this, PositionHelper.HalfCentimetre);

            Show();
            UpdateLayout();
            PositionHelper.PlaceAboveCursor(this, PositionHelper.HalfCentimetre);
            Activate();
            Focus();

            // make sure this panel really owns the foreground (tray clicks can be
            // forwarded by Explorer, which blocks a plain SetForegroundWindow)
            try
            {
                IntPtr hwnd = WindowMaterial.HandleOf(this);
                bool ok = NativeMethods.ForceForeground(hwnd);
                DiagLog.Write("foreground: " + (ok ? "acquired" : "refused"));
            }
            catch
            {
            }

            PlayOpenAnimation();
            _timer.Start();
        }

        /// <summary>
        /// Windows 11 style opening: the acrylic material fades up while the
        /// content slides in. The window geometry is never animated - that is what
        /// keeps the backdrop from flashing black.
        /// </summary>
        private void PlayOpenAnimation()
        {
            if (_contentHost == null)
            {
                return;
            }

            var transform = _contentHost.RenderTransform as TranslateTransform;
            var ease = new CubicBezierEase();

            _contentHost.Opacity = 1;
            var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(230));
            fade.EasingFunction = ease;
            fade.FillBehavior = FillBehavior.Stop;
            _contentHost.BeginAnimation(UIElement.OpacityProperty, fade);

            if (transform != null)
            {
                transform.Y = 0;
                var slide = new DoubleAnimation(22.0, 0.0, TimeSpan.FromMilliseconds(300));
                slide.EasingFunction = ease;
                slide.FillBehavior = FillBehavior.Stop;
                transform.BeginAnimation(TranslateTransform.YProperty, slide);
            }

        }

        public void HideFlyout()
        {
            if (!IsVisible || _closing)
            {
                return;
            }
            _closing = true;
            _timer.Stop();
            if (_overlay != null)
            {
                _overlay.Children.Clear();
            }
            PlayCloseAnimation();
        }

        /// <summary>Closing animation: the content sinks slightly and fades out.</summary>
        private void PlayCloseAnimation()
        {
            if (_contentHost == null)
            {
                Hide();
                _closing = false;
                return;
            }

            var transform = _contentHost.RenderTransform as TranslateTransform;
            var ease = new CubicBezierEase(0.55, 0.0, 0.85, 0.35);

            var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(150));
            fade.EasingFunction = ease;
            fade.FillBehavior = FillBehavior.Stop;
            fade.Completed += delegate
            {
                _contentHost.BeginAnimation(UIElement.OpacityProperty, null);
                if (transform != null)
                {
                    transform.BeginAnimation(TranslateTransform.YProperty, null);
                    transform.Y = 0;
                }
                _contentHost.Opacity = 1;
                Hide();
                _closing = false;
                ApplyMaterialStrength(1.0);
                UpdateLayoutForPage(false);
            };
            _contentHost.BeginAnimation(UIElement.OpacityProperty, fade);

            if (transform != null)
            {
                var sink = new DoubleAnimation(0.0, 14.0, TimeSpan.FromMilliseconds(160));
                sink.EasingFunction = ease;
                sink.FillBehavior = FillBehavior.Stop;
                transform.BeginAnimation(TranslateTransform.YProperty, sink);
            }

        }
        // ---------------------------------------------------------- navigation

        private void SetPage(FlyoutPage page, bool animate)
        {
            _currentPage = page;
            Grid target = PageGrid(page);
            foreach (Grid candidate in new[] { _mixerPage, _devicePage, _settingsPage })
            {
                candidate.Visibility = candidate == target ? Visibility.Visible : Visibility.Collapsed;
                candidate.Opacity = candidate == target ? 1 : 0;
                var transform = candidate.RenderTransform as TranslateTransform;
                if (transform != null)
                {
                    transform.X = 0;
                }
            }
            ApplyPageChrome(page);
            UpdateLayoutForPage(animate);
        }

        private void ApplyPageChrome(FlyoutPage page)
        {
            _deviceHeaderButton.Visibility = page == FlyoutPage.Mixer ? Visibility.Visible : Visibility.Collapsed;
            _pageTitlePanel.Visibility = page == FlyoutPage.Mixer ? Visibility.Collapsed : Visibility.Visible;
            _backButton.Visibility = page == FlyoutPage.Mixer ? Visibility.Collapsed : Visibility.Visible;
            _footerGrid.Visibility = page == FlyoutPage.Mixer ? Visibility.Visible : Visibility.Collapsed;
            _sparkleButton.Visibility = page == FlyoutPage.Settings ? Visibility.Visible : Visibility.Collapsed;

            if (page == FlyoutPage.Devices)
            {
                _pageTitleText.Text = L.T("page.devices.title");
                _pageSubtitleText.Text = L.T("page.devices.subtitle");
                _pageSubtitleText.Visibility = Visibility.Visible;
                RefreshDeviceList();
            }
            else if (page == FlyoutPage.Settings)
            {
                _pageTitleText.Text = L.T("page.settings.title");
                _pageSubtitleText.Text = L.T("page.settings.subtitle");
                _pageSubtitleText.Visibility = Visibility.Visible;
            }
        }

        private Grid PageGrid(FlyoutPage page)
        {
            switch (page)
            {
                case FlyoutPage.Devices: return _devicePage;
                case FlyoutPage.Settings: return _settingsPage;
                default: return _mixerPage;
            }
        }

        private void NavigateTo(FlyoutPage page)
        {
            if (page == _currentPage)
            {
                return;
            }

            Grid from = PageGrid(_currentPage);
            Grid to = PageGrid(page);
            bool forward = (int)page > (int)_currentPage;
            double offset = 26.0;
            var ease = new CubicBezierEase();

            to.Visibility = Visibility.Visible;
            to.Opacity = 0;
            var toTransform = to.RenderTransform as TranslateTransform;
            if (toTransform != null)
            {
                toTransform.X = forward ? offset : -offset;
                var moveIn = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240));
                moveIn.EasingFunction = ease;
                toTransform.BeginAnimation(TranslateTransform.XProperty, moveIn);
            }
            var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
            fadeIn.EasingFunction = ease;
            to.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            var fromTransform = from.RenderTransform as TranslateTransform;
            if (fromTransform != null)
            {
                var moveOut = new DoubleAnimation(forward ? -offset : offset, TimeSpan.FromMilliseconds(200));
                moveOut.EasingFunction = ease;
                fromTransform.BeginAnimation(TranslateTransform.XProperty, moveOut);
            }
            var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(160));
            fadeOut.EasingFunction = ease;
            fadeOut.Completed += delegate
            {
                if (_currentPage != PageOf(from))
                {
                    from.Visibility = Visibility.Collapsed;
                }
            };
            from.BeginAnimation(UIElement.OpacityProperty, fadeOut);

            _currentPage = page;
            ApplyPageChrome(page);

            _animating = true;
            _navigateGuard.Stop();
            _navigateGuard.Start();
            UpdateLayoutForPage(true);
            KeepWindowVisible(true);
        }

        private FlyoutPage PageOf(Grid page)
        {
            if (page == _devicePage)
            {
                return FlyoutPage.Devices;
            }
            if (page == _settingsPage)
            {
                return FlyoutPage.Settings;
            }
            return FlyoutPage.Mixer;
        }

        // --------------------------------------------------------------- layout

        private void UpdateScreenBudget()
        {
            if (_maxPageOverride > 0)
            {
                _maxPageHeight = _maxPageOverride;
                return;
            }
            double workHeight = PositionHelper.WorkAreaHeightNearCursor(this);
            double reserved = CardTopPadding + HeaderHeight + FooterHeight + 6 + 2 + 30;
            double available = workHeight - reserved;
            if (available < 150)
            {
                available = 150;
            }
            if (available > MaxPageHeightLimit)
            {
                available = MaxPageHeightLimit;
            }
            _maxPageHeight = available;
        }

        private void UpdateLayoutForPage(bool animate)
        {
            double settingsOffset = _settingsScroll != null ? _settingsScroll.VerticalOffset : 0.0;
            double pageHeight = ComputePageHeight(_currentPage);
            double total = CardTopPadding + HeaderHeight + pageHeight
                + (_footerGrid.Visibility == Visibility.Visible ? FooterHeight + 6 : 0) + 2;

            _pageHost.Height = pageHeight;
            if (_mixerScroll != null)
            {
                _mixerScroll.Height = Math.Max(40, pageHeight
                    - (_settings.ShowMasterVolume ? MixerRow.RowHeight + 9 : 0));
            }
            if (_deviceScroll != null)
            {
                _deviceScroll.Height = pageHeight;
            }
            if (_settingsScroll != null)
            {
                _settingsScroll.Height = pageHeight;
                if (settingsOffset > 0.0)
                {
                    _settingsScroll.ScrollToVerticalOffset(settingsOffset);
                }
            }

            AnimateWindowHeight(total, animate);
        }

        private void AnimateWindowHeight(double total, bool animate)
        {
            BeginAnimation(HeightProperty, null);
            if (!animate || Math.Abs(Height - total) < 0.5)
            {
                Height = total;
                KeepWindowVisible(false);
                return;
            }

            double from = Height;
            Height = total;
            var animation = new DoubleAnimation(from, total, TimeSpan.FromMilliseconds(220));
            animation.EasingFunction = new CubicBezierEase();
            animation.FillBehavior = FillBehavior.Stop;
            animation.Completed += delegate { KeepWindowVisible(true); };
            BeginAnimation(HeightProperty, animation);
        }

        /// <summary>
        /// Keeps the whole panel inside the work area after its height changed.
        /// The settings page is taller than the mixer, so it is also lifted by one
        /// centimetre (as requested) instead of growing off the bottom of the screen.
        /// </summary>
        public void KeepWindowVisible(bool allowLift)
        {
            try
            {
                POINT cursor;
                if (!NativeMethods.GetCursorPos(out cursor))
                {
                    return;
                }

                RECT work = NativeMethods.GetWorkAreaNear(cursor);
                double scale = 1.0;
                try
                {
                    DpiScale dpi = VisualTreeHelper.GetDpi(this);
                    if (dpi.DpiScaleX > 0)
                    {
                        scale = dpi.DpiScaleX;
                    }
                }
                catch
                {
                }

                double heightPx = Height * scale;
                double marginPx = 8 * scale;
                double topPx = Top * scale;

                if (allowLift && _currentPage == FlyoutPage.Settings)
                {
                    // one centimetre up, so the taller settings list stays fully visible
                    topPx -= 37.8 * scale;
                }

                double maxTop = work.Bottom - heightPx - marginPx;
                double minTop = work.Top + marginPx;
                if (maxTop < minTop)
                {
                    maxTop = minTop;
                }
                if (topPx > maxTop)
                {
                    topPx = maxTop;
                }
                if (topPx < minTop)
                {
                    topPx = minTop;
                }

                Top = topPx / scale;
            }
            catch
            {
            }
        }

        private double ComputePageHeight(FlyoutPage page)
        {
            double limit = _maxPageHeight;

            if (page == FlyoutPage.Devices)
            {
                int count = Math.Max(_devices.Count, 1);
                return Clamp((count * DeviceRowHeight) + 12, MinPageHeight, limit);
            }
            if (page == FlyoutPage.Settings)
            {
                return Clamp(SettingsContentHeight(), MinPageHeight, limit);
            }

            double list = 0;
            foreach (KeyValuePair<string, MixerRow> pair in _rows)
            {
                list += MixerRow.RowHeight;
            }
            if (list <= 0)
            {
                list = 84;
            }
            double master = _settings.ShowMasterVolume ? MixerRow.RowHeight + 9 : 0;
            return Clamp(master + list, MinPageHeight, limit);
        }

        /// <summary>Cached height of the settings content; re-measured only when the page is rebuilt.</summary>
        private double SettingsContentHeight()
        {
            if (_settingsContentHeight > 1.0)
            {
                return _settingsContentHeight;
            }
            if (_settingsHost != null)
            {
                double available = Math.Max(120, WindowWidth - (2 * PageSideMargin) - 16);
                _settingsHost.Measure(new Size(available, double.PositiveInfinity));
                double desired = _settingsHost.DesiredSize.Height;
                if (desired > 10)
                {
                    _settingsContentHeight = desired + 4;
                    return _settingsContentHeight;
                }
            }
            return (5 * SettingRowHeight) + (2 * ChoiceRowHeight) + 46 + 10 + SettingRowHeight + 96 + 12;
        }

        private static double Clamp(double value, double min, double max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }

        // -------------------------------------------------------------- content

        public void Refresh()
        {
            if (_themeDirty)
            {
                _themeDirty = false;
                ApplyTheme();
            }

            try
            {
                AudioDevice device = _engine.GetDefaultDevice();
                if (device != null)
                {
                    _deviceId = device.Id;
                    _deviceName = device.Name;
                    _deviceNameText.Text = device.Name;
                    _deviceNameText.ToolTip = device.Name;
                }
                else if (_deviceNameText != null && string.IsNullOrEmpty(_deviceId))
                {
                    _deviceNameText.Text = L.T("device.none");
                }

                if (_settings.ShowMasterVolume)
                {
                    UpdateMasterRow();
                }

                List<AudioSessionModel> sessions = _engine.GetSessions(_deviceId);
                ReconcileRows(sessions);
                UpdateLayoutForPage(false);
            }
            catch
            {
            }
        }

        private void UpdateMasterRow()
        {
            if (_masterRow == null || string.IsNullOrEmpty(_deviceId))
            {
                return;
            }
            var model = _masterRow.Model;
            model.Volume = (int)Math.Round(_engine.GetDeviceVolume(_deviceId) * 100.0);
            model.IsMuted = _engine.GetDeviceMute(_deviceId);
            _masterRow.Apply(model);
        }

        private void ReconcileRows(List<AudioSessionModel> sessions)
        {
            if (_masterHost != null)
            {
                _masterHost.Visibility = _settings.ShowMasterVolume ? Visibility.Visible : Visibility.Collapsed;
                _masterDivider.Visibility = _settings.ShowMasterVolume ? Visibility.Visible : Visibility.Collapsed;
            }

            var seen = new Dictionary<string, AudioSessionModel>();
            for (int i = 0; i < sessions.Count; i++)
            {
                AudioSessionModel model = sessions[i];
                if (model.IsSystemSounds && !_settings.ShowSystemSounds)
                {
                    continue;
                }
                if (_settings.HideInactive && !model.IsActive)
                {
                    continue;
                }
                seen[model.Key] = model;
            }

            var removals = new List<string>();
            foreach (KeyValuePair<string, MixerRow> pair in _rows)
            {
                if (!seen.ContainsKey(pair.Key))
                {
                    removals.Add(pair.Key);
                }
            }
            for (int i = 0; i < removals.Count; i++)
            {
                RemoveRow(removals[i]);
            }

            int index = 0;
            foreach (KeyValuePair<string, AudioSessionModel> pair in seen)
            {
                MixerRow row;
                if (_rows.TryGetValue(pair.Key, out row))
                {
                    if (!row.Dragging)
                    {
                        SyncModel(row, pair.Value);
                        row.Apply(row.Model);
                    }
                }
                else
                {
                    row = new MixerRow(_theme, pair.Value, OnVolumeChanged, OnMuteChanged);
                    _rows[pair.Key] = row;
                    _rowsHost.Children.Add(row.Root);
                    AnimateRowIn(row);
                }

                SetRowOrder(row, index);
                index++;
            }

            _rowOrder.Clear();
            for (int i = 0; i < _rowsHost.Children.Count; i++)
            {
                UIElement child = _rowsHost.Children[i];
                foreach (KeyValuePair<string, MixerRow> pair in _rows)
                {
                    if (pair.Value.Root == child)
                    {
                        _rowOrder.Add(pair.Value);
                        break;
                    }
                }
            }

            _emptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private static void SyncModel(MixerRow row, AudioSessionModel fresh)
        {
            if (row.Model == null)
            {
                row.Model = fresh;
                return;
            }
            row.Model.Controls = fresh.Controls;
            row.Model.Volume = fresh.Volume;
            row.Model.IsMuted = fresh.IsMuted;
            row.Model.IsActive = fresh.IsActive;
            row.Model.Name = fresh.Name;
            row.Model.ImagePath = fresh.ImagePath;
        }

        private void SetRowOrder(MixerRow row, int index)
        {
            int current = _rowsHost.Children.IndexOf(row.Root);
            int offset = _emptyText.Visibility == Visibility.Visible ? 1 : 0;
            int desired = index + offset;
            if (current >= 0 && current != desired)
            {
                _rowsHost.Children.Remove(row.Root);
                int target = Math.Min(desired, _rowsHost.Children.Count);
                _rowsHost.Children.Insert(target, row.Root);
            }
        }

        private void RemoveRow(string key)
        {
            MixerRow row;
            if (!_rows.TryGetValue(key, out row))
            {
                return;
            }
            _rows.Remove(key);
            if (row.Dragging)
            {
                _rowsHost.Children.Remove(row.Root);
                return;
            }

            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fade.EasingFunction = UiKit.FluentAccelerate();
            fade.Completed += delegate { _rowsHost.Children.Remove(row.Root); };
            row.Root.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        private void AnimateRowIn(MixerRow row)
        {
            row.Root.Opacity = 0;
            var transform = row.Root.RenderTransform as TranslateTransform;
            if (transform != null)
            {
                transform.Y = 8;
                var move = new DoubleAnimation(0, TimeSpan.FromMilliseconds(220));
                move.EasingFunction = new CubicBezierEase();
                transform.BeginAnimation(TranslateTransform.YProperty, move);
            }
            var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));
            fade.EasingFunction = new CubicBezierEase();
            row.Root.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        /// <summary>Screen position of a mixer entry, used by the screenshot diagnostics.</summary>
        public bool TryGetRowScreenPoint(int index, out Point point)
        {
            point = new Point();
            FrameworkElement element = null;
            if (index <= 0)
            {
                if (_masterRow != null)
                {
                    element = _masterRow.Root;
                }
            }
            else if (index - 1 < _rowOrder.Count)
            {
                element = _rowOrder[index - 1].Root;
            }
            if (element == null || element.ActualWidth < 1)
            {
                return false;
            }
            try
            {
                point = element.PointToScreen(new Point(element.ActualWidth * 0.55, element.ActualHeight / 2));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Diagnostics: cap the page height to simulate a small screen.</summary>
        public void SetMaxPageOverride(double height)
        {
            _maxPageOverride = height;
            if (height > 0)
            {
                _maxPageHeight = height;
            }
            UpdateLayoutForPage(false);
        }

        /// <summary>Diagnostics: screen position of the settings scrollbar thumb.</summary>
        public bool TryGetScrollThumbScreenPoint(out Point point)
        {
            point = new Point();
            if (_settingsScroll == null || _settingsScroll.Template == null)
            {
                return false;
            }
            try
            {
                var bar = _settingsScroll.Template.FindName("PART_VerticalScrollBar", _settingsScroll) as ScrollBar;
                if (bar == null || bar.Template == null)
                {
                    return false;
                }
                var track = bar.Template.FindName("PART_Track", bar) as Track;
                if (track == null || track.Thumb == null || track.Thumb.ActualHeight < 2)
                {
                    return false;
                }
                point = track.Thumb.PointToScreen(new Point(track.Thumb.ActualWidth / 2, track.Thumb.ActualHeight / 2));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Diagnostics: screen position of a mixer row thumb centre.</summary>
        public bool TryGetThumbScreenPoint(int rowIndex, out Point point)
        {
            point = new Point();
            MixerRow row = null;
            if (rowIndex <= 0)
            {
                row = _masterRow;
            }
            else if (rowIndex - 1 < _rowOrder.Count)
            {
                row = _rowOrder[rowIndex - 1];
            }
            if (row == null || row.Volume.ActualWidth < 1)
            {
                return false;
            }
            try
            {
                VolumeSlider slider = row.Volume;
                double range = slider.Maximum - slider.Minimum;
                double ratio = range <= 0 ? 0 : (slider.Value - slider.Minimum) / range;
                double x = (VolumeSlider.ThumbSlot / 2.0) + ((slider.ActualWidth - VolumeSlider.ThumbSlot) * ratio);
                point = slider.PointToScreen(new Point(x, slider.ActualHeight / 2.0));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Diagnostics: screen position of the centre of the current page.</summary>
        public bool TryGetPageScreenPoint(out Point point)
        {
            point = new Point();
            if (_pageHost == null || _pageHost.ActualWidth < 1)
            {
                return false;
            }
            try
            {
                point = _pageHost.PointToScreen(new Point(_pageHost.ActualWidth / 2, _pageHost.ActualHeight / 2));
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Screen position of the easter egg button (diagnostics only).</summary>
        public bool TryGetSparkleScreenPoint(out Point point)
        {
            point = new Point();
            if (_sparkleButton == null || _sparkleButton.ActualWidth < 1)
            {
                return false;
            }
            try
            {
                point = _sparkleButton.PointToScreen(new Point(_sparkleButton.ActualWidth / 2, _sparkleButton.ActualHeight / 2));
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---------------------------------------------------------- device list

        private void RefreshDeviceList()
        {
            _devices = _engine.GetPlaybackDevices();
            _deviceHost.Children.Clear();
            _deviceButtons.Clear();

            AudioDevice current = _engine.GetDefaultDevice();
            string currentId = current != null ? current.Id : _deviceId;

            for (int i = 0; i < _devices.Count; i++)
            {
                AudioDevice device = _devices[i];
                _deviceHost.Children.Add(CreateDeviceRow(device, device.Id == currentId));
            }
        }

        private Button CreateDeviceRow(AudioDevice device, bool isCurrent)
        {
            string deviceId = device.Id;
            var button = new Button();
            button.Height = DeviceRowHeight;
            button.Template = UiKit.RowButtonTemplate(_theme);
            button.Cursor = Cursors.Hand;
            button.ToolTip = device.Name;

            var content = new Grid();
            content.Margin = new Thickness(10, 0, 10, 0);
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            TextBlock glyph = UiKit.Text(GlyphDevice, isCurrent ? _theme.Accent : _theme.TextSecondary,
                16, FontWeights.Normal, true);
            glyph.Width = 26;
            glyph.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(glyph, 0);
            content.Children.Add(glyph);

            TextBlock name = UiKit.Text(device.Name, _theme.Text, InterfaceFont.Body, FontWeights.Normal, false);
            name.Margin = new Thickness(8, 0, 8, 0);
            name.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(name, 1);
            content.Children.Add(name);

            TextBlock check = UiKit.Text(GlyphCheck, _theme.Accent, 14, FontWeights.Normal, true);
            check.VerticalAlignment = VerticalAlignment.Center;
            check.Visibility = isCurrent ? Visibility.Visible : Visibility.Collapsed;
            Grid.SetColumn(check, 2);
            content.Children.Add(check);

            button.Content = content;
            button.Click += delegate { SwitchDevice(deviceId); };
            _deviceButtons[deviceId] = button;
            return button;
        }

        private void SwitchDevice(string deviceId)
        {
            try
            {
                _engine.SetDefaultDevice(deviceId);
            }
            catch
            {
            }
            _deviceId = deviceId;
            RefreshDeviceList();
            Refresh();
            RaiseDeviceMuteChanged();
            NavigateTo(FlyoutPage.Mixer);
        }

        // -------------------------------------------------------------- theming

        public void ApplyTheme()
        {
            double settingsOffset = _settingsScroll != null ? _settingsScroll.VerticalOffset : 0.0;
            double deviceOffset = _deviceScroll != null ? _deviceScroll.VerticalOffset : 0.0;

            Content = null;
            _rows.Clear();
            _rowOrder.Clear();
            _deviceButtons.Clear();
            _appearanceRadios.Clear();
            _languageRadios.Clear();
            BuildUi();
            ApplyBackdrop();
            SetPage(_currentPage, false);
            Refresh();
            if (IsVisible)
            {
                UpdateLayout();
            }

            // changing the language or the theme rebuilds the page: put the scroll
            // position back so the list does not jump to the top
            if (settingsOffset > 0.0 || deviceOffset > 0.0)
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(delegate
                {
                    try
                    {
                        if (settingsOffset > 0.0 && _settingsScroll != null)
                        {
                            _settingsScroll.ScrollToVerticalOffset(settingsOffset);
                        }
                        if (deviceOffset > 0.0 && _deviceScroll != null)
                        {
                            _deviceScroll.ScrollToVerticalOffset(deviceOffset);
                        }
                    }
                    catch
                    {
                    }
                }));
            }
        }

        /// <summary>Diagnostics: reload the page exactly like a language or theme change does.</summary>
        public void DebugReloadUi()
        {
            ApplyTheme();
        }

        public void MarkThemeDirty()
        {
            _themeDirty = true;
        }

        /// <summary>Diagnostics: force the opaque surface so the bitmap capture shows the layout.</summary>
        public void ForceSolidSurface()
        {
            _acrylicActive = false;
            UpdateCardSurface();
        }

        public void TriggerEgg()
        {
            if (_overlay != null)
            {
                FluentKit.SpawnEgg(_overlay);
            }
        }

        public string DeviceName
        {
            get { return _deviceName; }
        }

        public string DeviceId
        {
            get { return _deviceId; }
        }

        public int SessionRowCount
        {
            get { return _rows.Count; }
        }

        // ----------------------------------------------------------- easter egg

        private void OnSparkleClick(object sender, RoutedEventArgs e)
        {
            PopSparkle();
            _sparkleClicks++;
            if (_sparkleClicks < SparkleClicksToTrigger)
            {
                return;
            }
            _sparkleClicks = 0;
            if (_overlay != null)
            {
                FluentKit.SpawnEgg(_overlay);
            }
        }

        private void PopSparkle()
        {
            var content = _sparkleButton.Content as FrameworkElement;
            if (content == null)
            {
                return;
            }

            var transform = content.RenderTransform as ScaleTransform;
            if (transform == null)
            {
                transform = new ScaleTransform(1, 1);
                content.RenderTransformOrigin = new Point(0.5, 0.5);
                content.RenderTransform = transform;
            }

            var pop = new DoubleAnimationUsingKeyFrames();
            pop.Duration = TimeSpan.FromMilliseconds(340);
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.0)));
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(1.45, KeyTime.FromPercent(0.35)));
            pop.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0)));
            transform.BeginAnimation(ScaleTransform.ScaleXProperty, pop);
            transform.BeginAnimation(ScaleTransform.ScaleYProperty, pop);
        }
    }
}
























