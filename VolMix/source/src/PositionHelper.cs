using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace VolMix
{
    /// <summary>
    /// Places the flyout above the pointer, offset to the right like EarTrumpet
    /// does: the bottom left corner sits half a centimetre up and to the right of
    /// the cursor. The window is only shifted as far as needed to stay on screen.
    /// </summary>
    internal static class PositionHelper
    {
        private const int MdtEffectiveDpi = 0;

        /// <summary>Half a centimetre in device independent pixels.</summary>
        public const double HalfCentimetre = 18.9;

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        /// <summary>
        /// Positions the flyout. Called before the window is shown the correction
        /// pass is skipped (there is no HWND yet), which is what prevents the first
        /// frame from flashing in the top left corner.
        /// </summary>
        public static void PlaceAboveCursor(Window window, double gap)
        {
            POINT cursor;
            if (!NativeMethods.GetCursorPos(out cursor))
            {
                return;
            }

            IntPtr monitor = NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST);
            double scale = ScaleFor(monitor, window);
            RECT work = NativeMethods.GetWorkAreaNear(cursor);

            double widthPx = window.Width * scale;
            double heightPx = window.Height * scale;
            double gapPx = gap * scale;
            double marginPx = 8 * scale;

            // bottom left corner of the flyout goes half a centimetre above/right of the pointer
            double left = cursor.X + gapPx;
            double top = cursor.Y - heightPx - gapPx;

            double minLeft = work.Left + marginPx;
            double maxLeft = work.Right - widthPx - marginPx;
            if (maxLeft < minLeft)
            {
                maxLeft = minLeft;
            }
            if (left > maxLeft)
            {
                left = maxLeft;
            }
            if (left < minLeft)
            {
                left = minLeft;
            }

            double minTop = work.Top + marginPx;
            double maxTop = work.Bottom - heightPx - marginPx;
            if (maxTop < minTop)
            {
                maxTop = minTop;
            }
            if (top < minTop)
            {
                // taskbar on top: open below the pointer instead
                top = cursor.Y + gapPx;
            }
            if (top > maxTop)
            {
                top = maxTop;
            }
            if (top < minTop)
            {
                top = minTop;
            }

            MoveTo(window, left, top, scale);
        }

        private static void MoveTo(Window window, double leftPx, double topPx, double scale)
        {
            window.Left = leftPx / scale;
            window.Top = topPx / scale;

            if (!window.IsVisible)
            {
                return;
            }

            window.UpdateLayout();
            try
            {
                Point actual = window.PointToScreen(new Point(0, 0));
                double deltaX = leftPx - actual.X;
                double deltaY = topPx - actual.Y;
                if (Math.Abs(deltaX) > 1.0 || Math.Abs(deltaY) > 1.0)
                {
                    window.Left = window.Left + (deltaX / scale);
                    window.Top = window.Top + (deltaY / scale);
                }
            }
            catch
            {
            }
        }

        private static double ScaleFor(IntPtr monitor, Window window)
        {
            try
            {
                uint dpiX;
                uint dpiY;
                if (monitor != IntPtr.Zero
                    && GetDpiForMonitor(monitor, MdtEffectiveDpi, out dpiX, out dpiY) == 0
                    && dpiX > 0)
                {
                    return dpiX / 96.0;
                }
                DpiScale dpi = VisualTreeHelper.GetDpi(window);
                if (dpi.DpiScaleX > 0)
                {
                    return dpi.DpiScaleX;
                }
            }
            catch
            {
            }
            return 1.0;
        }

        /// <summary>Usable height (in DIP) of the monitor under the pointer.</summary>
        public static double WorkAreaHeightNearCursor(Window window)
        {
            POINT cursor;
            if (!NativeMethods.GetCursorPos(out cursor))
            {
                return 720.0;
            }
            RECT work = NativeMethods.GetWorkAreaNear(cursor);
            double scale = ScaleFor(NativeMethods.MonitorFromPoint(cursor, NativeMethods.MONITOR_DEFAULTTONEAREST), window);
            return (work.Bottom - work.Top) / scale;
        }
    }
}

