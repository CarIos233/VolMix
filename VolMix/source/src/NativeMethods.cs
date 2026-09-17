using System;
using System.Runtime.InteropServices;
using System.Text;

namespace VolMix
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct MONITORINFO
    {
        public int cbSize;
        public RECT rcMonitor;
        public RECT rcWork;
        public uint dwFlags;
    }

    /// <summary>ACCENT_POLICY used by SetWindowCompositionAttribute.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct ACCENTPOLICY
    {
        public int AccentState;
        public int AccentFlags;
        public int GradientColor;
        public int AnimationId;
    }

    /// <summary>WINDOWCOMPOSITIONATTRIBDATA used by SetWindowCompositionAttribute.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct WINDOWCOMPOSITIONATTRIBDATA
    {
        public int Attribute;
        public IntPtr Data;
        public int SizeOfData;
    }

    /// <summary>MSLLHOOKSTRUCT for the low level mouse hook.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct MSLLHOOKSTRUCT
    {
        public POINT pt;
        public int mouseData;
        public int flags;
        public int time;
        public IntPtr dwExtraInfo;
    }

    internal static class NativeMethods
    {
        public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        public const int WCA_ACCENT_POLICY = 19;
        public const int ACCENT_ENABLE_BLURBEHIND = 3;
        public const int ACCENT_ENABLE_ACRYLICBLURBEHIND = 4;

        // DWM window attributes
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        public const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        public const int DWMWA_BORDER_COLOR = 34;
        public const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        public const int DWMWCP_DEFAULT = 0;
        public const int DWMWCP_DONOTROUND = 1;
        public const int DWMWCP_ROUND = 2;
        public const int DWMWCP_ROUNDSMALL = 3;

        // system backdrop types (Windows 11 22H2 and later)
        public const int DWMSBT_AUTO = 0;
        public const int DWMSBT_NONE = 1;
        public const int DWMSBT_MAINWINDOW = 2;      // Mica
        public const int DWMSBT_TRANSIENTWINDOW = 3; // Acrylic
        public const int DWMSBT_TABBEDWINDOW = 4;

        public const int MONITOR_DEFAULTTONEAREST = 2;
        public const int SM_CXSMICON = 49;
        public const int SM_CYSMICON = 50;

        [DllImport("dwmapi.dll", PreserveSig = true)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int valueSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool DestroyIcon(IntPtr hIcon);

        [DllImport("ole32.dll")]
        public static extern int PropVariantClear(ref PROPVARIANT variant);

        [DllImport("user32.dll")]
        public static extern bool GetCursorPos(out POINT point);

        [DllImport("user32.dll")]
        public static extern bool SetCursorPos(int x, int y);

        [DllImport("user32.dll")]
        public static extern IntPtr MonitorFromPoint(POINT point, uint flags);

        [DllImport("user32.dll", EntryPoint = "GetMonitorInfoW")]
        public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        public static extern int GetSystemMetrics(int index);

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);

        public const uint MOUSEEVENTF_WHEEL = 0x0800;
        public const uint MOUSEEVENTF_MOVE = 0x0001;
        public const uint MOUSEEVENTF_LEFTDOWN = 0x0002;
        public const uint MOUSEEVENTF_LEFTUP = 0x0004;

        public const int WH_MOUSE_LL = 14;
        public const byte VK_MENU = 0x12;
        public const uint KEYEVENTF_KEYUP = 0x0002;

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr SetWindowsHookEx(int hookId, LowLevelMouseProc callback, IntPtr module, uint threadId);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnhookWindowsHookEx(IntPtr hook);

        [DllImport("user32.dll")]
        public static extern IntPtr CallNextHookEx(IntPtr hook, int code, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        public static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);

        [DllImport("user32.dll")]
        public static extern uint GetWindowThreadProcessId(IntPtr hwnd, IntPtr processId);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint attach, uint attachTo, bool attachFlag);

        [DllImport("user32.dll")]
        public static extern IntPtr SetActiveWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern IntPtr SetFocus(IntPtr hwnd);

        [DllImport("user32.dll")]
        public static extern void keybd_event(byte virtualKey, byte scanCode, uint flags, IntPtr extraInfo);

        /// <summary>
        /// Brings the window to the front even when this process does not own the
        /// foreground. Uses AttachThreadInput and falls back to the ALT key trick.
        /// </summary>
        public static bool ForceForeground(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            IntPtr foreground = GetForegroundWindow();
            if (foreground == hwnd)
            {
                return true;
            }

            uint foregroundThread = foreground == IntPtr.Zero ? 0 : GetWindowThreadProcessId(foreground, IntPtr.Zero);
            uint ourThread = GetCurrentThreadId();
            bool attached = false;

            try
            {
                if (foregroundThread != 0 && foregroundThread != ourThread)
                {
                    attached = AttachThreadInput(foregroundThread, ourThread, true);
                }
                SetForegroundWindow(hwnd);
                SetActiveWindow(hwnd);
                SetFocus(hwnd);
            }
            catch
            {
            }
            finally
            {
                if (attached)
                {
                    try
                    {
                        AttachThreadInput(foregroundThread, ourThread, false);
                    }
                    catch
                    {
                    }
                }
            }

            if (GetForegroundWindow() == hwnd)
            {
                return true;
            }

            return GetForegroundWindow() == hwnd;
        }

        [DllImport("user32.dll")]
        public static extern void mouse_event(uint flags, int dx, int dy, int data, IntPtr extraInfo);

        public const uint WM_MOUSEWHEEL = 0x020A;

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        public static extern bool PostMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern int QueryDosDevice(string deviceName, StringBuilder targetPath, int max);

        /// <summary>Work area of the monitor that contains the given screen point.</summary>
        public static RECT GetWorkAreaNear(POINT point)
        {
            IntPtr monitor = MonitorFromPoint(point, MONITOR_DEFAULTTONEAREST);
            var info = new MONITORINFO();
            info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
            if (monitor != IntPtr.Zero && GetMonitorInfo(monitor, ref info))
            {
                return info.rcWork;
            }
            return new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1080 };
        }

        /// <summary>Turns \Device\HarddiskVolumeN\... into a normal drive path.</summary>
        public static string DevicePathToDosPath(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath))
            {
                return null;
            }
            if (devicePath.Length > 1 && devicePath[1] == ':')
            {
                return devicePath;
            }
            if (!devicePath.StartsWith(@"\Device\", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            int end = devicePath.IndexOf('\\', 8);
            if (end < 0)
            {
                return null;
            }
            string device = devicePath.Substring(0, end);
            string rest = devicePath.Substring(end);

            string mapped = DosDeviceCache.Resolve(device);
            if (mapped == null)
            {
                return null;
            }
            return mapped + rest;
        }
    }

    /// <summary>Maps \Device\HarddiskVolumeN to a drive letter (cached).</summary>
    internal static class DosDeviceCache
    {
        private static readonly System.Collections.Generic.Dictionary<string, string> Map =
            new System.Collections.Generic.Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private static bool _enumerated;

        public static string Resolve(string device)
        {
            lock (Map)
            {
                if (!_enumerated)
                {
                    Enumerate();
                    _enumerated = true;
                }
                string mapped;
                if (Map.TryGetValue(device, out mapped))
                {
                    return mapped;
                }
            }
            return null;
        }

        private static void Enumerate()
        {
            var buffer = new StringBuilder(1024);
            for (char letter = 'A'; letter <= 'Z'; letter++)
            {
                string drive = letter + ":";
                try
                {
                    int length = NativeMethods.QueryDosDevice(drive, buffer, buffer.Capacity);
                    if (length > 0)
                    {
                        string target = buffer.ToString();
                        if (!string.IsNullOrEmpty(target) && !Map.ContainsKey(target))
                        {
                            Map[target] = drive;
                        }
                    }
                }
                catch
                {
                }
            }
        }
    }
}






