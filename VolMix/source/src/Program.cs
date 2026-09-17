using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace VolMix
{
    internal static class Program
    {
        private const string MutexName = "Local\\VolMix.SingleInstance";
        private const string ActivateEventName = "Local\\VolMix.Activate";

        [STAThread]
        public static int Main(string[] args)
        {
            string logPath = ArgumentValue(args, "--log");
            bool diagnostics = logPath != null
                || HasFlag(args, "--probe")
                || HasFlag(args, "--selftest")
                || HasFlag(args, "--smoke")
                || HasFlag(args, "--checkquiet")
                || HasFlag(args, "--switchtest")
                || ArgumentValue(args, "--capture") != null
                || ArgumentValue(args, "--screencapture") != null;
            if (diagnostics)
            {
                DiagLog.Open(logPath);
            }

            try
            {
                string langArgument = ArgumentValue(args, "--lang");
                if (!string.IsNullOrEmpty(langArgument))
                {
                    if (langArgument.StartsWith("en", StringComparison.OrdinalIgnoreCase))
                    {
                        L.Apply(AppLanguage.English);
                    }
                    else if (langArgument.StartsWith("zh", StringComparison.OrdinalIgnoreCase))
                    {
                        L.Apply(AppLanguage.Chinese);
                    }
                    else
                    {
                        L.Apply(AppLanguage.Auto);
                    }
                }

                if (HasFlag(args, "--probe"))
                {
                    return Diagnostics.RunProbe();
                }
                if (HasFlag(args, "--selftest"))
                {
                    return Diagnostics.RunSelfTest();
                }
                if (HasFlag(args, "--switchtest"))
                {
                    return Diagnostics.RunSwitchTest();
                }
                string trayPreview = ArgumentValue(args, "--traypreview");
                if (!string.IsNullOrEmpty(trayPreview))
                {
                    return Diagnostics.RunTrayPreview(trayPreview);
                }

                string screenCapture = ArgumentValue(args, "--screencapture");
                if (!string.IsNullOrEmpty(screenCapture))
                {
                    return Diagnostics.RunScreenCapture(screenCapture, ArgumentValue(args, "--page"),
                        ArgumentValue(args, "--theme"), ArgumentValue(args, "--wait"), HasFlag(args, "--egg"),
                        ArgumentValue(args, "--material"), ArgumentValue(args, "--maxpage"),
                        ArgumentValue(args, "--wheel"), HasFlag(args, "--nohide"),
                        HasFlag(args, "--wheelreal"), DragRowArgument(args), HasFlag(args, "--reload"));
                }

                string capture = ArgumentValue(args, "--capture");
                if (!string.IsNullOrEmpty(capture))
                {
                    return Diagnostics.RunCapture(capture, ArgumentValue(args, "--page"),
                        ArgumentValue(args, "--theme"), ArgumentValue(args, "--hover"));
                }
            }
            catch (Exception ex)
            {
                DiagLog.Write("diagnostics failed: " + ex);
                DiagLog.Flush();
                return 3;
            }

            return RunApplication(args);
        }

        private static int RunApplication(string[] args)
        {
            bool createdNew;
            var mutex = new Mutex(true, MutexName, out createdNew);
            if (!createdNew)
            {
                // a launch caused by the autostart entry must never open the panel;
                // only a launch the user performed on purpose does
                if (!HasFlag(args, "--startup"))
                {
                    try
                    {
                        EventWaitHandle signal;
                        if (EventWaitHandle.TryOpenExisting(ActivateEventName, out signal))
                        {
                            using (signal)
                            {
                                signal.Set();
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                return 0;
            }

            EventWaitHandle activate = null;
            try
            {
                activate = new EventWaitHandle(false, EventResetMode.AutoReset, ActivateEventName);
            }
            catch
            {
            }

            bool firstRun = !File.Exists(Settings.FilePath);
            L.Apply(Settings.Load().Language);

            // clear a signal that may be left over from a previous session, so the
            // panel cannot pop up on its own when the app starts with Windows
            if (activate != null)
            {
                try
                {
                    activate.Reset();
                }
                catch
                {
                }
            }

            var app = new VolMixApp();
            var systemTheme = new SystemThemeWatcher(app);
            try
            {
                app.Start(activate, firstRun);
                if (HasFlag(args, "--checkquiet"))
                {
                    app.RunQuietCheck(delegate(string message) { DiagLog.Write(message); });
                }
                if (HasFlag(args, "--smoke"))
                {
                    app.RunSmokeTest(delegate(string message) { DiagLog.Write(message); });
                }
                app.Run();
            }
            catch (Exception ex)
            {
                try
                {
                    DiagLog.Write("fatal: " + ex);
                    DiagLog.Flush();
                }
                catch
                {
                }
                return 1;
            }
            finally
            {
                systemTheme.Dispose();
                if (activate != null)
                {
                    activate.Dispose();
                }
                GC.KeepAlive(mutex);
            }
            return 0;
        }

        // ------------------------------------------------------------- helpers

        private static int DragRowArgument(string[] args)
        {
            int value = 0;
            string text = ArgumentValue(args, "--drag");
            if (!string.IsNullOrEmpty(text))
            {
                int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }
            return value;
        }

        private static bool HasFlag(string[] args, string name)
        {
            for (int i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        private static string ArgumentValue(string[] args, string name)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i + 1];
                }
            }
            return null;
        }
    }

    /// <summary>Watches for Windows light/dark changes while the app runs.</summary>
    internal sealed class SystemThemeWatcher : IDisposable
    {
        private readonly VolMixApp _app;

        public SystemThemeWatcher(VolMixApp app)
        {
            _app = app;
            try
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged += OnUserPreferenceChanged;
            }
            catch
            {
            }
        }

        private void OnUserPreferenceChanged(object sender, Microsoft.Win32.UserPreferenceChangedEventArgs e)
        {
            _app.RefreshThemeFromSystem();
        }

        public void Dispose()
        {
            try
            {
                Microsoft.Win32.SystemEvents.UserPreferenceChanged -= OnUserPreferenceChanged;
            }
            catch
            {
            }
        }
    }

    /// <summary>Diagnostics work in the same process so they use production code paths.</summary>
    internal static class Diagnostics
    {
        private static readonly List<string> Lines = new List<string>();

        private static void Say(string text)
        {
            Lines.Add(text);
            DiagLog.Write(text);
        }

        public static int RunProbe()
        {
            Say("VolMix diagnostics " + Settings.VersionText);
            Say("64 bit: " + (IntPtr.Size == 8));

            var engine = new AudioEngine();
            List<AudioDevice> devices = engine.GetPlaybackDevices();
            Say("devices: " + devices.Count);
            for (int i = 0; i < devices.Count; i++)
            {
                Say("  " + devices[i].Name + " :: " + devices[i].Id);
            }

            AudioDevice current = engine.GetDefaultDevice();
            Say("default: " + (current == null ? "<none>" : current.Name));
            if (current != null)
            {
                Say("master volume: " + Math.Round(engine.GetDeviceVolume(current.Id) * 100) + "%");
                Say("master muted: " + engine.GetDeviceMute(current.Id));
                List<AudioSessionModel> sessions = engine.GetSessions(current.Id);
                Say("sessions: " + sessions.Count);
                for (int i = 0; i < sessions.Count; i++)
                {
                    AudioSessionModel session = sessions[i];
                    Say(string.Format(CultureInfo.InvariantCulture,
                        "  [{0}] pid={1} vol={2}% mute={3} active={4} system={5} controls={6}",
                        session.Name, session.ProcessId, session.Volume, session.IsMuted,
                        session.IsActive, session.IsSystemSounds, session.Controls.Count));
                    Say("      image: " + (session.ImagePath ?? "<none>"));
                }
            }

            DiagLog.Flush();
            return 0;
        }

        /// <summary>
        /// Switches the default playback device to another endpoint, checks the
        /// result and restores the previous one. Verifies the IPolicyConfig path
        /// end to end.
        /// </summary>
        public static int RunSwitchTest()
        {
            var engine = new AudioEngine();
            AudioDevice before = engine.GetDefaultDevice();
            if (before == null)
            {
                Say("no default device");
                DiagLog.Flush();
                return 1;
            }
            Say("current default: " + before.Name);

            List<AudioDevice> devices = engine.GetPlaybackDevices();
            AudioDevice target = null;
            for (int i = 0; i < devices.Count; i++)
            {
                if (devices[i].Id != before.Id)
                {
                    target = devices[i];
                    break;
                }
            }
            if (target == null)
            {
                Say("no alternative device to switch to");
                DiagLog.Flush();
                return 0;
            }

            Say("switching to: " + target.Name);
            engine.SetDefaultDevice(target.Id);
            Thread.Sleep(600);
            AudioDevice middle = engine.GetDefaultDevice();
            bool switched = middle != null && middle.Id == target.Id;
            Say("default now: " + (middle == null ? "<none>" : middle.Name) + " switched=" + switched);

            Say("restoring: " + before.Name);
            for (int attempt = 0; attempt < 3; attempt++)
            {
                engine.SetDefaultDevice(before.Id);
                Thread.Sleep(600);
                AudioDevice restored = engine.GetDefaultDevice();
                if (restored != null && restored.Id == before.Id)
                {
                    Say("restored: " + restored.Name);
                    DiagLog.Flush();
                    return switched ? 0 : 2;
                }
                Say("restore attempt " + (attempt + 1) + " failed, retrying");
            }

            Say("RESTORE FAILED - current default: " + (engine.GetDefaultDevice() == null
                ? "<none>" : engine.GetDefaultDevice().Name));
            DiagLog.Flush();
            return 3;
        }

        public static int RunSelfTest()
        {
            int failures = 0;
            Say("VolMix self test " + Settings.VersionText);

            // templates
            var theme = new VoltTheme(true);
            try
            {
                UiKit.SliderTemplate(theme);
                UiKit.ToggleTemplate(theme);
                UiKit.IconButtonTemplate(theme);
                UiKit.RowButtonTemplate(theme);
                UiKit.MenuItemTemplate(theme);
                UiKit.ScrollBarStyle(theme);
                UiKit.ToolTipStyle(theme);
                Say("templates(dark): ok");
            }
            catch (Exception ex)
            {
                failures++;
                Say("templates(dark): FAILED " + ex.Message);
            }

            theme.Apply(false);
            try
            {
                UiKit.SliderTemplate(theme);
                UiKit.ToggleTemplate(theme);
                UiKit.ToolTipStyle(theme);
                Say("templates(light, regenerated): ok generation=" + theme.Generation);
            }
            catch (Exception ex)
            {
                failures++;
                Say("templates(light): FAILED " + ex.Message);
            }

            // tray icon
            try
            {
                IntPtr handle;
                System.Drawing.Icon icon = IconHelper.CreateTrayIcon(out handle, false,
                    VoltThemeFactory.IsLightTaskbar());
                Say("tray icon: ok size=" + icon.Width + "x" + icon.Height + " handle=" + handle);
                IconHelper.ReleaseIcon(handle);
            }
            catch (Exception ex)
            {
                failures++;
                Say("tray icon: FAILED " + ex.Message);
            }

            // settings round trip
            try
            {
                var settings = Settings.Load();
                bool originalSounds = settings.ShowSystemSounds;
                AppearanceMode originalAppearance = settings.Appearance;
                settings.ShowSystemSounds = !originalSounds;
                settings.Appearance = originalAppearance == AppearanceMode.Dark
                    ? AppearanceMode.Light
                    : AppearanceMode.Dark;
                settings.Save();
                var reloaded = Settings.Load();
                bool ok = reloaded.ShowSystemSounds == settings.ShowSystemSounds
                    && reloaded.Appearance == settings.Appearance;
                settings.ShowSystemSounds = originalSounds;
                settings.Appearance = originalAppearance;
                settings.Save();
                Say("settings round trip: " + (ok ? "ok" : "FAILED")
                    + " (appearance=" + reloaded.Appearance + ")");
                if (!ok)
                {
                    failures++;
                }
            }
            catch (Exception ex)
            {
                failures++;
                Say("settings: FAILED " + ex.Message);
            }

            // startup entry toggle (restores the previous state)
            try
            {
                bool before = Settings.GetStartWithWindows();
                bool set = Settings.SetStartWithWindows(true);
                bool during = Settings.GetStartWithWindows();
                Settings.SetStartWithWindows(before);
                bool after = Settings.GetStartWithWindows();
                Say("startup toggle: set=" + set + " present=" + during + " restored=" + (after == before));
            }
            catch (Exception ex)
            {
                Say("startup toggle: exception " + ex.Message);
            }

            // start menu shortcut create/remove (restores the previous state)
            try
            {
                bool existed = StartMenuShortcut.Exists();
                bool created = StartMenuShortcut.Set(true);
                bool present = StartMenuShortcut.Exists();
                if (!existed)
                {
                    StartMenuShortcut.Remove();
                }
                bool restored = StartMenuShortcut.Exists() == existed;
                Say("start menu shortcut: created=" + created + " present=" + present + " restored=" + restored);
            }
            catch (Exception ex)
            {
                Say("start menu shortcut: exception " + ex.Message);
            }

            // session volume write/restore through the same code path the UI uses
            try
            {
                var engine = new AudioEngine();
                AudioDevice device = engine.GetDefaultDevice();
                string sessionResult = "no session";
                if (device != null)
                {
                    List<AudioSessionModel> sessions = engine.GetSessions(device.Id);
                    for (int i = 0; i < sessions.Count; i++)
                    {
                        AudioSessionModel session = sessions[i];
                        if (session.IsSystemSounds || session.Controls.Count == 0)
                        {
                            continue;
                        }
                        int original = session.Volume;
                        int target = original > 50 ? 30 : 70;
                        engine.SetSessionVolume(session, target / 100.0f);
                        int readBack = engine.GetSessions(device.Id).Find(
                            delegate(AudioSessionModel candidate) { return candidate.Key == session.Key; }).Volume;
                        engine.SetSessionVolume(session, original / 100.0f);
                        sessionResult = session.Name + ": " + original + "% -> " + readBack + "% -> restored";
                        break;
                    }
                    Say("session volume write: " + sessionResult);
                }
            }
            catch (Exception ex)
            {
                failures++;
                Say("session volume write: FAILED " + ex.Message);
            }

            Say(failures == 0 ? "RESULT: PASS" : "RESULT: FAIL (" + failures + ")");
            DiagLog.Flush();
            return failures == 0 ? 0 : 1;
        }

        public static int RunCapture(string path, string page, string themeName, string hoverIndex)
        {
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var settings = Settings.Load();
            bool dark = settings.ResolveDark(VoltThemeFactory.IsSystemDark());
            if (!string.IsNullOrEmpty(themeName))
            {
                dark = !themeName.StartsWith("light", StringComparison.OrdinalIgnoreCase);
            }
            var theme = new VoltTheme(dark);
            var engine = new AudioEngine();
            var flyout = new FlyoutWindow(engine, theme, settings);

            FlyoutPage target = FlyoutPage.Mixer;
            if (!string.IsNullOrEmpty(page) && page.StartsWith("dev", StringComparison.OrdinalIgnoreCase))
            {
                target = FlyoutPage.Devices;
            }
            else if (!string.IsNullOrEmpty(page) && page.StartsWith("set", StringComparison.OrdinalIgnoreCase))
            {
                target = FlyoutPage.Settings;
            }

            int hover = -1;
            if (!string.IsNullOrEmpty(hoverIndex))
            {
                int parsed;
                if (int.TryParse(hoverIndex, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed))
                {
                    hover = parsed;
                }
            }

            POINT original = new POINT();
            bool movedCursor = false;
            int exitCode = 0;
            Say("capture request: page=" + target + " theme=" + (dark ? "dark" : "light")
                + " hover=" + hover);
            POINT probe;
            Say("cursor query: " + (NativeMethods.GetCursorPos(out probe)
                ? probe.X + "," + probe.Y : "FAILED"));

            var showTimer = new DispatcherTimer();
            showTimer.Interval = TimeSpan.FromMilliseconds(900);
            showTimer.Tick += delegate
            {
                showTimer.Stop();
                try
                {
                    flyout.ForceSolidSurface();
                    flyout.ShowFlyout(target);
                    flyout.UpdateLayout();
                    Say("shown, page=" + target);
                }
                catch (Exception ex)
                {
                    exitCode = 2;
                    Say("show failed: " + ex);
                    DiagLog.Flush();
                    app.Shutdown();
                }
            };
            showTimer.Start();

            var captureTimer = new DispatcherTimer();
            captureTimer.Interval = TimeSpan.FromMilliseconds(hover >= 0 ? 1700 : 1500);
            captureTimer.Tick += delegate
            {
                captureTimer.Stop();
                try
                {
                    if (hover >= 0 && NativeMethods.GetCursorPos(out original))
                    {
                        Point point;
                        if (flyout.TryGetRowScreenPoint(hover, out point))
                        {
                            NativeMethods.SetCursorPos((int)point.X, (int)point.Y);
                            movedCursor = true;
                            Say("hovering row " + hover + " at " + (int)point.X + "," + (int)point.Y);
                            POINT check;
                            if (NativeMethods.GetCursorPos(out check))
                            {
                                Say("cursor now at " + check.X + "," + check.Y);
                            }
                        }
                        else
                        {
                            Say("row " + hover + " not found");
                        }
                    }
                }
                catch (Exception ex)
                {
                    Say("hover failed: " + ex.Message);
                }

                var renderTimer = new DispatcherTimer();
                renderTimer.Interval = TimeSpan.FromMilliseconds(hover >= 0 ? 600 : 0);
                renderTimer.Tick += delegate
                {
                    renderTimer.Stop();
                    try
                    {
                        RenderWindow(flyout, path);
                        Say("captured " + path);
                    }
                    catch (Exception ex)
                    {
                        exitCode = 2;
                        Say("capture failed: " + ex);
                    }
                    if (movedCursor)
                    {
                        NativeMethods.SetCursorPos(original.X, original.Y);
                    }
                    DiagLog.Flush();
                    app.Shutdown();
                };
                renderTimer.Start();
            };
            captureTimer.Start();

            app.Run();
            return exitCode;
        }

        /// <summary>
        /// Renders the notification area glyph in all four variants (taskbar dark /
        /// light x muted / normal) scaled up, so the artwork can be reviewed.
        /// </summary>
        public static int RunTrayPreview(string path)
        {
            int small = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
            if (small < 16)
            {
                small = 16;
            }
            int scale = 4;
            int cell = small * scale;

            var variants = new[]
            {
                IconHelper.RenderStatusGlyph(small, false, false),
                IconHelper.RenderStatusGlyph(small, true, false),
                IconHelper.RenderStatusGlyph(small, false, true),
                IconHelper.RenderStatusGlyph(small, true, true)
            };

            var visual = new DrawingVisual();
            using (DrawingContext context = visual.RenderOpen())
            {
                var dark = new SolidColorBrush(Color.FromRgb(0x20, 0x20, 0x20));
                dark.Freeze();
                context.DrawRectangle(dark, null, new Rect(0, 0, cell * 2, cell));
                context.DrawRectangle(Brushes.White, null, new Rect(cell * 2, 0, cell * 2, cell));
                for (int i = 0; i < variants.Length; i++)
                {
                    context.DrawImage(variants[i], new Rect(i * cell, 0, cell, cell));
                }
            }

            var bitmap = new RenderTargetBitmap(cell * 4, cell, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
            Say("tray preview written: " + path + " (icon size " + small + ")");
            DiagLog.Flush();
            return 0;
        }

        /// <summary>
        /// Grabs the real pixels of the screen region under the flyout, so the DWM
        /// acrylic backdrop, the round corners and the shadow are visible.
        /// </summary>
        public static int RunScreenCapture(string path, string page, string themeName, string waitMs, bool egg,
            string material, string maxPage, string wheelCount, bool keepVisible, bool realWheel, int dragRow,
            bool reloadUi)
        {
            var app = new Application();
            app.ShutdownMode = ShutdownMode.OnExplicitShutdown;
            var settings = Settings.Load();
            bool dark = settings.ResolveDark(VoltThemeFactory.IsSystemDark());
            if (!string.IsNullOrEmpty(themeName))
            {
                dark = !themeName.StartsWith("light", StringComparison.OrdinalIgnoreCase);
            }

            var theme = new VoltTheme(dark);
            WindowMaterial.Forced = ParseMaterial(material);
            var engine = new AudioEngine();
            var flyout = new FlyoutWindow(engine, theme, settings);
            if (keepVisible)
            {
                flyout.SuppressAutoHide = true;
            }

            double pageOverride = ParseNumber(maxPage, 0);
            if (pageOverride > 0)
            {
                flyout.SetMaxPageOverride(pageOverride);
            }

            int wheelSteps = (int)ParseNumber(wheelCount, 0);
            int wait = (int)ParseNumber(waitMs, 1000);

            FlyoutPage target = FlyoutPage.Mixer;
            if (!string.IsNullOrEmpty(page) && page.StartsWith("dev", StringComparison.OrdinalIgnoreCase))
            {
                target = FlyoutPage.Devices;
            }
            else if (!string.IsNullOrEmpty(page) && page.StartsWith("set", StringComparison.OrdinalIgnoreCase))
            {
                target = FlyoutPage.Settings;
            }

            int exitCode = 0;
            POINT savedCursor = new POINT();
            bool cursorMoved = false;

            var showTimer = new DispatcherTimer();
            showTimer.Interval = TimeSpan.FromMilliseconds(600);
            showTimer.Tick += delegate
            {
                showTimer.Stop();
                try
                {
                    flyout.ShowFlyout(target);
                    if (egg)
                    {
                        flyout.TriggerEgg();
                    }
                    POINT pointer;
                    NativeMethods.GetCursorPos(out pointer);
                    Say("screen capture: page=" + target + " acrylic=" + flyout.AcrylicActive
                        + " material=" + WindowMaterial.Forced);
                    Say("  window: " + Math.Round(flyout.Left) + "," + Math.Round(flyout.Top)
                        + " size=" + Math.Round(flyout.Width) + "x" + Math.Round(flyout.Height)
                        + " pointer=" + pointer.X + "," + pointer.Y);
                }
                catch (Exception ex)
                {
                    exitCode = 2;
                    Say("show failed: " + ex);
                }

                // wheel steps, driven by a timer so the UI thread keeps rendering
                if (wheelSteps != 0)
                {
                    int remaining = Math.Abs(wheelSteps);
                    var wheelTimer = new DispatcherTimer();
                    wheelTimer.Interval = TimeSpan.FromMilliseconds(320);
                    wheelTimer.Tick += delegate
                    {
                        wheelTimer.Stop();
                        Point pagePoint;
                        if (!flyout.TryGetPageScreenPoint(out pagePoint))
                        {
                            return;
                        }

                        if (!cursorMoved)
                        {
                            NativeMethods.GetCursorPos(out savedCursor);
                            cursorMoved = true;
                        }
                        NativeMethods.SetCursorPos((int)Math.Round(pagePoint.X), (int)Math.Round(pagePoint.Y));

                        if (realWheel)
                        {
                            int notches = wheelSteps > 0 ? 1 : -1;
                            NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_WHEEL, 0, 0, notches * 120, IntPtr.Zero);
                        }
                        else
                        {
                            IntPtr panel = WindowMaterial.HandleOf(flyout);
                            short delta = (short)(wheelSteps > 0 ? 120 : -120);
                            int lparam = (((int)Math.Round(pagePoint.Y) & 0xFFFF) << 16) | ((int)Math.Round(pagePoint.X) & 0xFFFF);
                            NativeMethods.PostMessage(panel, NativeMethods.WM_MOUSEWHEEL,
                                new IntPtr(((int)(ushort)delta) << 16), new IntPtr(lparam));
                        }
                        Say("  posted wheel, offset=" + Math.Round(flyout.DebugScrollOffset, 1)
                            + " scrollable=" + Math.Round(flyout.DebugScrollableHeight, 1));

                        remaining--;
                        if (remaining > 0)
                        {
                            wheelTimer.Start();
                        }
                    };
                    wheelTimer.Start();
                }

                if (dragRow != 0)
                {
                    int dragStep = 0;
                    Point thumbPoint = new Point();
                    bool gotThumb = false;
                    var dragTimer = new DispatcherTimer();
                    dragTimer.Interval = TimeSpan.FromMilliseconds(150);
                    dragTimer.Tick += delegate
                    {
                        try
                        {
                            if (dragStep == 0)
                            {
                                gotThumb = dragRow < 0
                                    ? flyout.TryGetScrollThumbScreenPoint(out thumbPoint)
                                    : flyout.TryGetThumbScreenPoint(dragRow, out thumbPoint);
                                if (!gotThumb)
                                {
                                    Say("drag: thumb not found");
                                    dragTimer.Stop();
                                    return;
                                }
                                NativeMethods.GetCursorPos(out savedCursor);
                                cursorMoved = true;
                                NativeMethods.SetCursorPos((int)Math.Round(thumbPoint.X), (int)Math.Round(thumbPoint.Y));
                                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTDOWN, 0, 0, 0, IntPtr.Zero);
                                Say("drag: press at " + (int)Math.Round(thumbPoint.X) + "," + (int)Math.Round(thumbPoint.Y));
                            }
                            else if (dragStep == 1 || dragStep == 2)
                            {
                                int stepX = dragRow < 0 ? 0 : (dragStep * 45);
                                int stepY = dragRow < 0 ? (dragStep * 45) : 0;
                                NativeMethods.SetCursorPos((int)Math.Round(thumbPoint.X) + stepX,
                                    (int)Math.Round(thumbPoint.Y) + stepY);
                                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_MOVE, 0, 0, 0, IntPtr.Zero);
                                Say("drag: move step " + dragStep);
                            }
                            else if (dragStep == 3)
                            {
                                NativeMethods.mouse_event(NativeMethods.MOUSEEVENTF_LEFTUP, 0, 0, 0, IntPtr.Zero);
                                Say("drag: release");
                            }
                            else
                            {
                                NativeMethods.SetCursorPos((int)Math.Round(thumbPoint.X),
                                    (int)Math.Round(thumbPoint.Y) + 240);
                                Say("drag: cursor moved away");
                                dragTimer.Stop();
                                return;
                            }
                            dragStep++;
                        }
                        catch (Exception ex)
                        {
                            Say("drag failed: " + ex.Message);
                            dragTimer.Stop();
                        }
                    };
                    dragTimer.Start();
                }

                if (reloadUi)
                {
                    var reloadTimer = new DispatcherTimer();
                    reloadTimer.Interval = TimeSpan.FromMilliseconds(700);
                    reloadTimer.Tick += delegate
                    {
                        reloadTimer.Stop();
                        double before = flyout.DebugScrollOffset;
                        flyout.DebugReloadUi();
                        Say("reload: offset before=" + Math.Round(before, 1)
                            + " after=" + Math.Round(flyout.DebugScrollOffset, 1));
                    };
                    reloadTimer.Start();
                }

                int delay = 260 + Math.Max(0, wait) + (Math.Abs(wheelSteps) * 220) + (reloadUi ? 1400 : 0);
                var shotTimer = new DispatcherTimer();
                shotTimer.Interval = TimeSpan.FromMilliseconds(delay);
                shotTimer.Tick += delegate
                {
                    shotTimer.Stop();
                    try
                    {
                        Say("  at capture: theme=" + (theme.Dark ? "dark" : "light")
                            + " acrylic=" + flyout.AcrylicActive
                            + " offset=" + Math.Round(flyout.DebugScrollOffset, 1)
                            + " scrollable=" + Math.Round(flyout.DebugScrollableHeight, 1));
                        SaveScreenRegion(flyout, path);
                        Say("screen capture written: " + path);
                    }
                    catch (Exception ex)
                    {
                        exitCode = 3;
                        Say("screen capture failed: " + ex);
                    }
                    if (cursorMoved)
                    {
                        NativeMethods.SetCursorPos(savedCursor.X, savedCursor.Y);
                    }
                    DiagLog.Flush();
                    app.Shutdown();
                };
                shotTimer.Start();
            };
            showTimer.Start();
            app.Run();
            return exitCode;
        }

        private static double ParseNumber(string text, double fallback)
        {
            double value;
            if (!string.IsNullOrEmpty(text)
                && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
            {
                return value;
            }
            return fallback;
        }
        private static MaterialMode ParseMaterial(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return MaterialMode.Auto;
            }
            if (name.StartsWith("acc", StringComparison.OrdinalIgnoreCase))
            {
                return MaterialMode.Acrylic;
            }
            if (name.StartsWith("sys", StringComparison.OrdinalIgnoreCase))
            {
                return MaterialMode.SystemBackdrop;
            }
            if (name.StartsWith("sol", StringComparison.OrdinalIgnoreCase))
            {
                return MaterialMode.Solid;
            }
            return MaterialMode.Auto;
        }

        private static void SaveScreenRegion(Window window, string path)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(window);
            double scale = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
            Point origin = window.PointToScreen(new Point(0, 0));
            int width = (int)Math.Ceiling(window.ActualWidth * scale);
            int height = (int)Math.Ceiling(window.ActualHeight * scale);
            int pad = (int)Math.Ceiling(28 * scale);
            int left = (int)Math.Round(origin.X) - pad;
            int top = (int)Math.Round(origin.Y) - pad;
            int totalWidth = width + (pad * 2);
            int totalHeight = height + (pad * 2);

            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            Say("  capture region: left=" + left + " top=" + top + " pad=" + pad + " scale=" + scale);
            using (var bitmap = new System.Drawing.Bitmap(totalWidth, totalHeight,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb))
            {
                using (System.Drawing.Graphics graphics = System.Drawing.Graphics.FromImage(bitmap))
                {
                    graphics.CopyFromScreen(left, top, 0, 0,
                        new System.Drawing.Size(totalWidth, totalHeight),
                        System.Drawing.CopyPixelOperation.SourceCopy);
                }
                bitmap.Save(path, System.Drawing.Imaging.ImageFormat.Png);
            }
        }

        private static void RenderWindow(Window window, string path)
        {
            DpiScale dpi = VisualTreeHelper.GetDpi(window);
            double scale = dpi.DpiScaleX <= 0 ? 1.0 : dpi.DpiScaleX;
            int width = (int)Math.Ceiling(window.ActualWidth * scale);
            int height = (int)Math.Ceiling(window.ActualHeight * scale);
            if (width < 8 || height < 8)
            {
                throw new InvalidOperationException("window has no size");
            }

            var visual = (Visual)window.Content;
            var bitmap = new RenderTargetBitmap(width, height, 96 * scale, 96 * scale, PixelFormats.Pbgra32);
            bitmap.Render(visual);

            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));

            string folder = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(folder) && !Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }
            using (FileStream stream = File.Create(path))
            {
                encoder.Save(stream);
            }
        }
    }

    /// <summary>Writes diagnostics to a log file and to the parent console when there is one.</summary>
    internal static class DiagLog
    {
        private static StreamWriter _writer;
        private static string _path;

        public static void Open(string path)
        {
            try
            {
                _path = string.IsNullOrEmpty(path)
                    ? Path.Combine(Path.GetTempPath(), "volmix.log")
                    : path;
                _writer = new StreamWriter(_path, false);
                _writer.AutoFlush = true;
            }
            catch
            {
                _writer = null;
            }
        }

        public static void Write(string text)
        {
            try
            {
                Console.WriteLine(text);
            }
            catch
            {
            }
            try
            {
                if (_writer != null)
                {
                    _writer.WriteLine(text);
                }
            }
            catch
            {
            }
        }

        public static void Flush()
        {
            try
            {
                if (_writer != null)
                {
                    _writer.Flush();
                }
            }
            catch
            {
            }
        }

        public static string Path_
        {
            get { return _path; }
        }
    }
}



























