using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace VolMix
{
    internal enum MaterialMode
    {
        Auto = 0,
        Acrylic = 1,
        SystemBackdrop = 2,
        Solid = 3
    }

    /// <summary>
    /// Window background material. The classic acrylic accent is preferred
    /// because its tint opacity can be tuned, which is what produces the
    /// translucent look; the DWM system backdrop is the fallback.
    /// </summary>
    internal static class WindowMaterial
    {
        /// <summary>Diagnostics override (--material).</summary>
        public static MaterialMode Forced = MaterialMode.Auto;

        /// <summary>Last gradient colour that was handed to the acrylic accent.</summary>
        public static int LastGradient;

        public static bool Apply(Window window, VoltTheme theme, double strength)
        {
            IntPtr hwnd = HandleOf(window);
            if (hwnd == IntPtr.Zero)
            {
                return false;
            }

            ApplyFrame(hwnd, theme);

            if (Forced == MaterialMode.Solid)
            {
                return false;
            }

            if (Forced == MaterialMode.SystemBackdrop)
            {
                return ApplySystemBackdrop(hwnd);
            }

            bool acrylic = ApplyAcrylic(hwnd, theme, strength);
            if (acrylic && Forced == MaterialMode.Acrylic)
            {
                return true;
            }
            if (acrylic)
            {
                return true;
            }
            return ApplySystemBackdrop(hwnd);
        }

        public static IntPtr HandleOf(Window window)
        {
            try
            {
                return new WindowInteropHelper(window).Handle;
            }
            catch
            {
                return IntPtr.Zero;
            }
        }

        /// <summary>Round corners plus the matching light/dark window frame.</summary>
        public static void ApplyFrame(IntPtr hwnd, VoltTheme theme)
        {
            try
            {
                int round = NativeMethods.DWMWCP_ROUND;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_WINDOW_CORNER_PREFERENCE, ref round, 4);
                int dark = theme.Dark ? 1 : 0;
                NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, 4);
            }
            catch
            {
            }
        }

        private static bool ApplyAcrylic(IntPtr hwnd, VoltTheme theme, double strength)
        {
            try
            {
                if (strength < 0.02)
                {
                    strength = 0.02;
                }
                if (strength > 1.0)
                {
                    strength = 1.0;
                }

                int alpha = (int)Math.Round(theme.MaterialAlpha * strength);
                if (alpha < 1)
                {
                    alpha = 1;
                }
                if (alpha > 255)
                {
                    alpha = 255;
                }

                Color tint = theme.MaterialTint;
                int gradient = (alpha << 24) | (tint.B << 16) | (tint.G << 8) | tint.R;
                LastGradient = gradient;

                var policy = new ACCENTPOLICY();
                policy.AccentState = NativeMethods.ACCENT_ENABLE_ACRYLICBLURBEHIND;
                policy.AccentFlags = 2;
                policy.GradientColor = gradient;
                policy.AnimationId = 0;

                IntPtr buffer = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ACCENTPOLICY)));
                try
                {
                    Marshal.StructureToPtr(policy, buffer, false);
                    var data = new WINDOWCOMPOSITIONATTRIBDATA();
                    data.Attribute = NativeMethods.WCA_ACCENT_POLICY;
                    data.Data = buffer;
                    data.SizeOfData = Marshal.SizeOf(typeof(ACCENTPOLICY));
                    int result = NativeMethods.SetWindowCompositionAttribute(hwnd, ref data);
                    return result != 0;
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch
            {
                return false;
            }
        }

        private static bool ApplySystemBackdrop(IntPtr hwnd)
        {
            try
            {
                int backdrop = NativeMethods.DWMSBT_TRANSIENTWINDOW;
                int hr = NativeMethods.DwmSetWindowAttribute(hwnd, NativeMethods.DWMWA_SYSTEMBACKDROP_TYPE, ref backdrop, 4);
                return hr == 0;
            }
            catch
            {
                return false;
            }
        }
    }
}

