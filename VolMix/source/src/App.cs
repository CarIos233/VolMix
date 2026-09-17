using System;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace VolMix
{
    /// <summary>
    /// Hosts the notification area icon and owns the lifetime of the flyout window.
    /// </summary>
    internal sealed class VolMixApp : Application
    {
        private readonly AudioEngine _engine = new AudioEngine();
        private readonly Settings _settings = Settings.Load();
        private readonly VoltTheme _theme = VoltThemeFactory.Shared;

        private WinForms.NotifyIcon _tray;
        private IntPtr _trayIconHandle = IntPtr.Zero;
        private FlyoutWindow _flyout;
        private TrayMenuWindow _menu;
        private EventWaitHandle _activateEvent;
        private RegisteredWaitHandle _activateWait;
        private bool _shuttingDown;

        public VolMixApp()
        {
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            DispatcherUnhandledException += OnUnhandledException;
        }

        public void Start(EventWaitHandle activateEvent, bool firstRun)
        {
            _activateEvent = activateEvent;

            ApplyThemeFromSettings();

            _flyout = new FlyoutWindow(_engine, _theme, _settings);
            _flyout.ExitRequested += delegate { Shutdown(); };
            _flyout.DeviceMuteChanged += delegate { UpdateTrayIcon(); };

            _menu = CreateMenu();

            CreateTrayIcon();
            _settings.Changed += delegate { Dispatcher.BeginInvoke(new Action(RefreshTheme)); };

            if (_activateEvent != null)
            {
                _activateWait = ThreadPool.RegisterWaitForSingleObject(_activateEvent,
                    delegate { Dispatcher.BeginInvoke(new Action(ShowFromExternal)); }, null, -1, false);
            }

            // VolMix is a tray-only app: nothing is shown automatically, not even on
            // the very first run. The panel only opens when the tray icon is clicked
            // (or when a second instance asks the running one to show it).
        }

        /// <summary>Diagnostics: verify that a normal start shows nothing.</summary>
        public void RunQuietCheck(Action<string> log)
        {
            Step(2500, delegate
            {
                log("quiet check: flyout visible=" + _flyout.IsVisible
                    + " menu visible=" + (_menu != null && _menu.IsVisible));
                Shutdown();
            });
        }

        private void ShowFromExternal()
        {
            if (_shuttingDown)
            {
                return;
            }
            _flyout.ShowFlyout(FlyoutPage.Mixer);
        }

        private TrayMenuWindow CreateMenu()
        {
            var menu = new TrayMenuWindow(_theme, _settings);
            menu.OpenMixerRequested += delegate { _flyout.ShowFlyout(FlyoutPage.Mixer); };
            menu.OpenSettingsRequested += delegate { _flyout.ShowFlyout(FlyoutPage.Settings); };
            menu.ExitRequested += delegate { Shutdown(); };
            menu.LanguageChanged += delegate { Dispatcher.BeginInvoke(new Action(RefreshTheme)); };
            return menu;
        }

        // ------------------------------------------------------ notification area

        private void CreateTrayIcon()
        {
            _tray = new WinForms.NotifyIcon();
            _tray.Text = L.T("tray.tooltip");
            _tray.Visible = true;
            _tray.MouseUp += OnTrayMouseUp;
            UpdateTrayIcon();
        }

        /// <summary>
        /// Draws the notification area icon the way Windows 11 does: a monochrome
        /// speaker glyph that follows the taskbar theme and the mute state.
        /// </summary>
        private void UpdateTrayIcon()
        {
            try
            {
                bool muted = false;
                string deviceId = _flyout != null ? _flyout.DeviceId : null;
                if (string.IsNullOrEmpty(deviceId))
                {
                    AudioDevice device = _engine.GetDefaultDevice();
                    if (device != null)
                    {
                        deviceId = device.Id;
                    }
                }
                if (!string.IsNullOrEmpty(deviceId))
                {
                    muted = _engine.GetDeviceMute(deviceId);
                }

                IntPtr handle;
                System.Drawing.Icon icon = IconHelper.CreateTrayIcon(out handle, muted,
                    VoltThemeFactory.IsLightTaskbar());

                IntPtr previous = _trayIconHandle;
                _trayIconHandle = handle;
                if (_tray != null)
                {
                    _tray.Icon = icon;
                }
                if (previous != IntPtr.Zero)
                {
                    IconHelper.ReleaseIcon(previous);
                }
            }
            catch
            {
            }
        }

        private void OnTrayMouseUp(object sender, WinForms.MouseEventArgs e)
        {
            if (_shuttingDown)
            {
                return;
            }
            if (e.Button == WinForms.MouseButtons.Left)
            {
                _menu.HideMenu();
                _flyout.ToggleFlyout();
            }
            else if (e.Button == WinForms.MouseButtons.Right)
            {
                _flyout.HideFlyout();
                _menu.ShowAtCursor();
            }
            else if (e.Button == WinForms.MouseButtons.Middle)
            {
                ToggleDeviceMute();
            }
        }

        private void ToggleDeviceMute()
        {
            try
            {
                string deviceId = _flyout.DeviceId;
                if (string.IsNullOrEmpty(deviceId))
                {
                    AudioDevice device = _engine.GetDefaultDevice();
                    if (device == null)
                    {
                        return;
                    }
                    deviceId = device.Id;
                }
                bool muted = _engine.GetDeviceMute(deviceId);
                _engine.SetDeviceMute(deviceId, !muted);
                UpdateTrayIcon();
            }
            catch
            {
            }
        }

        private void ShowBalloon(string title, string text)
        {
            try
            {
                if (_tray == null)
                {
                    return;
                }
                _tray.BalloonTipTitle = title;
                _tray.BalloonTipText = text;
                _tray.BalloonTipIcon = WinForms.ToolTipIcon.Info;
                _tray.ShowBalloonTip(4000);
            }
            catch
            {
            }
        }

        // --------------------------------------------------------------- theme

        private void ApplyThemeFromSettings()
        {
            L.Apply(_settings.Language);
            _theme.Apply(_settings.ResolveDark(VoltThemeFactory.IsSystemDark()));
        }

        private void RefreshTheme()
        {
            DiagLog.Write("theme refresh");
            ApplyThemeFromSettings();
            if (_flyout != null)
            {
                _flyout.ApplyTheme();
            }
            if (_menu != null)
            {
                // a closed window can never be shown again, so replace the menu
                TrayMenuWindow previous = _menu;
                _menu = CreateMenu();
                try
                {
                    previous.HideMenu();
                    previous.Close();
                }
                catch
                {
                }
            }
            if (_tray != null)
            {
                _tray.Text = L.T("tray.tooltip");
            }
            UpdateTrayIcon();
        }

        public void RefreshThemeFromSystem()
        {
            if (_settings.Appearance != AppearanceMode.FollowSystem)
            {
                return;
            }
            Dispatcher.BeginInvoke(new Action(RefreshTheme));
        }

        // -------------------------------------------------------- smoke testing

        /// <summary>
        /// End to end check of the tray lifecycle (icon, flyout, menu) used by
        /// the --smoke diagnostics switch.
        /// </summary>
        public void RunSmokeTest(Action<string> log)
        {
            log("tray icon visible=" + (_tray != null && _tray.Visible)
                + " handle=" + _trayIconHandle);

            Step(700, delegate
            {
                _flyout.ShowFlyout(FlyoutPage.Mixer);
                log("flyout shown: visible=" + _flyout.IsVisible
                    + " rows=" + _flyout.SessionRowCount
                    + " size=" + Math.Round(_flyout.Width) + "x" + Math.Round(_flyout.Height)
                    + " pos=" + Math.Round(_flyout.Left) + "," + Math.Round(_flyout.Top));
                log("device: " + (_flyout.DeviceId ?? "<none>") + " / " + (_flyout.DeviceName ?? "<none>"));
                log("acrylic backdrop: " + (_flyout.AcrylicActive ? "active" : "unavailable (solid fallback)"));
            });

            Step(1900, delegate
            {
                _flyout.HideFlyout();
                log("flyout hide requested");
            });

            Step(2800, delegate
            {
                log("flyout visible after hide=" + _flyout.IsVisible);
                _menu.ShowAtCursor();
                log("tray menu shown: visible=" + _menu.IsVisible
                    + " size=" + Math.Round(_menu.Width) + "x" + Math.Round(_menu.Height));
            });

            Step(3800, delegate
            {
                _menu.HideMenu();
                log("tray menu hidden");
            });

            Step(4400, delegate
            {
                log("smoke test complete");
                Shutdown();
            });
        }

        private void Step(int milliseconds, Action action)
        {
            var timer = new DispatcherTimer();
            timer.Interval = TimeSpan.FromMilliseconds(milliseconds);
            timer.Tick += delegate
            {
                timer.Stop();
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    DiagLog.Write("smoke step failed: " + ex);
                }
            };
            timer.Start();
        }

        // --------------------------------------------------------------- exit

        private void OnUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                string folder = Settings.Folder;
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                File.AppendAllText(Path.Combine(folder, "error.log"),
                    DateTime.Now.ToString("s") + " " + e.Exception + Environment.NewLine);
            }
            catch
            {
            }
            e.Handled = true;
        }

        protected override void OnExit(ExitEventArgs e)
        {
            _shuttingDown = true;
            try
            {
                if (_activateWait != null)
                {
                    _activateWait.Unregister(null);
                }
                if (_flyout != null)
                {
                    _flyout.ReleaseHooks();
                }
                if (_tray != null)
                {
                    _tray.Visible = false;
                    _tray.Dispose();
                }
                IconHelper.ReleaseIcon(_trayIconHandle);
                _settings.Save();
            }
            catch
            {
            }
            base.OnExit(e);
        }
    }
}






