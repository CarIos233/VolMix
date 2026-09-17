using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shell;

namespace VolMix
{
    /// <summary>
    /// The small Windows 11 style menu shown when the tray icon is right clicked.
    /// Uses the same acrylic material as the flyout and animates its content only,
    /// so the backdrop never flashes.
    /// </summary>
    internal sealed class TrayMenuWindow : Window
    {
        public const double MenuWidth = 214.0;
        private const double ItemHeight = 36.0;

        private readonly VoltTheme _theme;
        private readonly Settings _settings;
        private Border _card;
        private Grid _contentHost;
        private bool _acrylicActive;
        private bool _closing;

        public event EventHandler OpenMixerRequested;
        public event EventHandler OpenSettingsRequested;
        public event EventHandler ExitRequested;
        public event EventHandler LanguageChanged;

        public TrayMenuWindow(VoltTheme theme, Settings settings)
        {
            _theme = theme;
            _settings = settings;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = false;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            SizeToContent = SizeToContent.Manual;
            WindowStartupLocation = WindowStartupLocation.Manual;
            FontFamily = InterfaceFont.DefaultFamily;
            Width = MenuWidth;
            SnapsToDevicePixels = true;
            UseLayoutRounding = true;

            var chrome = new WindowChrome();
            chrome.CornerRadius = new CornerRadius(0);
            chrome.GlassFrameThickness = new Thickness(1);
            chrome.CaptionHeight = 0;
            chrome.ResizeBorderThickness = new Thickness(0);
            chrome.UseAeroCaptionButtons = false;
            WindowChrome.SetWindowChrome(this, chrome);

            Build();
            SourceInitialized += delegate { ApplyMaterial(1.0); };
            Deactivated += delegate { HideMenu(); };
            PreviewKeyDown += delegate(object sender, KeyEventArgs e)
            {
                if (e.Key == Key.Escape)
                {
                    HideMenu();
                }
            };
        }

        private void ApplyMaterial(double strength)
        {
            _acrylicActive = WindowMaterial.Apply(this, _theme, strength);
            if (_card != null)
            {
                _card.Background = _acrylicActive ? (Brush)_theme.Card : (Brush)_theme.CardSolid;
            }
        }

        private void Build()
        {
            Resources[typeof(ToolTip)] = UiKit.ToolTipStyle(_theme);

            _card = new Border();
            _card.CornerRadius = new CornerRadius(8);
            _card.BorderBrush = _theme.CardBorder;
            _card.BorderThickness = new Thickness(1);
            _card.Background = _theme.Card;

            _contentHost = new Grid();
            _contentHost.RenderTransform = new TranslateTransform();

            var panel = new StackPanel();
            panel.Margin = new Thickness(0, 6, 0, 6);

            panel.Children.Add(CreateItem("\uE767", L.T("menu.open"), false, delegate
            {
                HideMenu();
                Raise(OpenMixerRequested);
            }));
            panel.Children.Add(CreateItem("\uE713", L.T("menu.settings"), false, delegate
            {
                HideMenu();
                Raise(OpenSettingsRequested);
            }));
            panel.Children.Add(CreateItem("\uE8C1", L.IsEnglish ? L.T("menu.language.toZh") : L.T("menu.language.toEn"),
                false, delegate
                {
                    HideMenu();
                    Settings settings = Settings.Load();
                    settings.Language = L.IsEnglish ? AppLanguage.Chinese : AppLanguage.English;
                    settings.Save();
                    L.Apply(settings.Language);
                    Raise(LanguageChanged);
                }));
            panel.Children.Add(CreateDivider());
            panel.Children.Add(CreateItem("\uE7E8", L.T("menu.exit"), true, delegate
            {
                HideMenu();
                Raise(ExitRequested);
            }));

            _contentHost.Children.Add(panel);
            _card.Child = _contentHost;
            Content = _card;

            Height = 6 + (4 * ItemHeight) + 9 + 6 + 2;
        }

        private Button CreateItem(string glyph, string text, bool danger, Action action)
        {
            var button = new Button();
            button.Height = ItemHeight;
            button.Template = UiKit.MenuItemTemplate(_theme);
            button.Cursor = Cursors.Hand;

            var content = new Grid();
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            TextBlock icon = UiKit.Text(glyph, danger ? _theme.DangerText : _theme.Text,
                14, FontWeights.Normal, true);
            icon.HorizontalAlignment = HorizontalAlignment.Left;
            Grid.SetColumn(icon, 0);
            content.Children.Add(icon);

            TextBlock label = UiKit.Text(text, danger ? _theme.DangerText : _theme.Text,
                InterfaceFont.Body, FontWeights.Normal, false);
            label.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(label, 1);
            content.Children.Add(label);

            button.Content = content;
            button.Click += delegate { action(); };
            return button;
        }

        private Border CreateDivider()
        {
            var divider = new Border();
            divider.Height = 1;
            divider.Margin = new Thickness(12, 4, 12, 4);
            divider.Background = _theme.Divider;
            return divider;
        }

        private static void Raise(EventHandler handler)
        {
            if (handler != null)
            {
                handler(null, EventArgs.Empty);
            }
        }

        public void ShowAtCursor()
        {
            _closing = false;
            Show();
            UpdateLayout();
            PositionHelper.PlaceAboveCursor(this, PositionHelper.HalfCentimetre);
            Activate();

            var transform = _contentHost.RenderTransform as TranslateTransform;
            var ease = new CubicBezierEase();

            _contentHost.Opacity = 1;
            var fade = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(180));
            fade.EasingFunction = ease;
            fade.FillBehavior = FillBehavior.Stop;
            _contentHost.BeginAnimation(UIElement.OpacityProperty, fade);

            if (transform != null)
            {
                transform.Y = 0;
                var slide = new DoubleAnimation(14.0, 0.0, TimeSpan.FromMilliseconds(220));
                slide.EasingFunction = ease;
                slide.FillBehavior = FillBehavior.Stop;
                transform.BeginAnimation(TranslateTransform.YProperty, slide);
            }
        }

        public void HideMenu()
        {
            if (!IsVisible || _closing)
            {
                return;
            }
            _closing = true;

            var transform = _contentHost.RenderTransform as TranslateTransform;
            var ease = new CubicBezierEase(0.55, 0.0, 0.85, 0.35);
            var fade = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(120));
            fade.EasingFunction = ease;
            fade.FillBehavior = FillBehavior.Stop;
            fade.Completed += delegate
            {
                _contentHost.BeginAnimation(UIElement.OpacityProperty, null);
                _contentHost.Opacity = 1;
                if (transform != null)
                {
                    transform.BeginAnimation(TranslateTransform.YProperty, null);
                    transform.Y = 0;
                }
                Hide();
                _closing = false;
                ApplyMaterial(1.0);
            };
            _contentHost.BeginAnimation(UIElement.OpacityProperty, fade);
        }
    }
}




