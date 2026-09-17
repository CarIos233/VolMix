using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using Drawing = System.Drawing;
using InteropImaging = System.Windows.Interop.Imaging;
using Media = System.Windows.Media;
using MediaImaging = System.Windows.Media.Imaging;
using WPF = System.Windows;

namespace VolMix
{
    /// <summary>
    /// Produces the artwork used by the mixer: application icons, the fallback
    /// letter tiles, the "system sounds" glyph, the monochrome notification area
    /// icon and the colour application icon.
    /// </summary>
    internal static class IconHelper
    {
        private const string IconFont = "Segoe Fluent Icons, Segoe MDL2 Assets";
        private const string SpeakerGlyph = "\uE767";
        private const string MutedGlyph = "\uE74F";

        private static readonly Dictionary<string, Media.ImageSource> IconCache =
            new Dictionary<string, Media.ImageSource>(StringComparer.OrdinalIgnoreCase);

        // ------------------------------------------------------------ app icons

        public static Media.ImageSource GetIcon(string imagePath, string appName, bool systemSounds)
        {
            string cacheKey = systemSounds
                ? "#system"
                : (string.IsNullOrEmpty(imagePath) ? "#" + (appName == null ? string.Empty : appName) : imagePath);

            Media.ImageSource cached;
            if (IconCache.TryGetValue(cacheKey, out cached))
            {
                return cached;
            }

            Media.ImageSource result = null;
            if (systemSounds)
            {
                result = RenderGlyph(SpeakerGlyph, 28, 13, true);
            }
            else if (!string.IsNullOrEmpty(imagePath))
            {
                result = ExtractFromFile(imagePath);
            }

            if (result == null)
            {
                result = CreateLetterTile(appName);
            }

            if (result.CanFreeze)
            {
                result.Freeze();
            }
            IconCache[cacheKey] = result;
            return result;
        }

        private static Media.ImageSource ExtractFromFile(string imagePath)
        {
            try
            {
                if (!File.Exists(imagePath))
                {
                    return null;
                }

                using (Drawing.Icon icon = Drawing.Icon.ExtractAssociatedIcon(imagePath))
                {
                    if (icon == null)
                    {
                        return null;
                    }

                    Media.ImageSource source = InteropImaging.CreateBitmapSourceFromHIcon(
                        icon.Handle,
                        WPF.Int32Rect.Empty,
                        MediaImaging.BitmapSizeOptions.FromWidthAndHeight(32, 32));
                    return source;
                }
            }
            catch
            {
                return null;
            }
        }

        private static Media.ImageSource CreateLetterTile(string appName)
        {
            string letter = "?";
            if (!string.IsNullOrEmpty(appName))
            {
                letter = appName.Substring(0, 1).ToUpperInvariant();
            }

            const int size = 28;
            var visual = new Media.DrawingVisual();
            using (Media.DrawingContext context = visual.RenderOpen())
            {
                Media.Brush fill = VoltThemeFactory.Shared.IconBack;
                context.DrawRoundedRectangle(fill, null, new WPF.Rect(0, 0, size, size), 7, 7);

                var typeface = new Media.Typeface(InterfaceFont.DefaultFamily,
                    WPF.FontStyles.Normal, WPF.FontWeights.SemiBold, WPF.FontStretches.Normal);
                var text = CreateText(letter, typeface, 13, Media.Brushes.White);
                WPF.Point origin = new WPF.Point((size - text.Width) / 2.0, (size - text.Height) / 2.0);
                context.DrawText(text, origin);
            }

            var bitmap = new MediaImaging.RenderTargetBitmap(size, size, 96, 96, Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }

        // ---------------------------------------------------------------- glyphs

        /// <summary>Renders a Segoe Fluent Icons glyph into a bitmap.</summary>
        public static Media.ImageSource RenderGlyph(string glyph, int boxSize, double fontSize, bool palette)
        {
            var visual = new Media.DrawingVisual();
            using (Media.DrawingContext context = visual.RenderOpen())
            {
                if (palette)
                {
                    double radius = boxSize * 0.28;
                    var gradient = new Media.LinearGradientBrush();
                    gradient.StartPoint = new WPF.Point(0, 0);
                    gradient.EndPoint = new WPF.Point(1, 1);
                    gradient.GradientStops.Add(new Media.GradientStop(
                        (Media.Color)Media.ColorConverter.ConvertFromString("#2E7DF6"), 0));
                    gradient.GradientStops.Add(new Media.GradientStop(
                        (Media.Color)Media.ColorConverter.ConvertFromString("#7A4DE8"), 1));
                    context.DrawRoundedRectangle(gradient, null,
                        new WPF.Rect(0, 0, boxSize, boxSize), radius, radius);
                }

                var typeface = new Media.Typeface(new Media.FontFamily(IconFont),
                    WPF.FontStyles.Normal, WPF.FontWeights.Normal, WPF.FontStretches.Normal);
                var text = CreateText(glyph, typeface, fontSize, Media.Brushes.White);
                WPF.Point origin = new WPF.Point(
                    (boxSize - text.Width) / 2.0,
                    (boxSize - text.Height) / 2.0);
                context.DrawText(text, origin);
            }

            var bitmap = new MediaImaging.RenderTargetBitmap(boxSize, boxSize, 96, 96, Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            return bitmap;
        }

        private static Media.FormattedText CreateText(string value, Media.Typeface typeface, double size, Media.Brush brush)
        {
            return new Media.FormattedText(value, CultureInfo.InvariantCulture,
                WPF.FlowDirection.LeftToRight, typeface, size, brush, 1.0);
        }

        // ---------------------------------------------------- notification area

        /// <summary>
        /// Renders the notification area icon the way Windows 11 draws its own:
        /// a monochrome speaker glyph that matches the taskbar theme.
        /// </summary>
        public static MediaImaging.BitmapSource RenderStatusGlyph(int size, bool muted, bool lightTaskbar)
        {
            string glyph = muted ? MutedGlyph : SpeakerGlyph;
            var color = lightTaskbar
                ? Media.Color.FromRgb(0x1A, 0x1A, 0x1A)
                : Media.Colors.White;
            var brush = new Media.SolidColorBrush(color);
            brush.Freeze();

            var visual = new Media.DrawingVisual();
            using (Media.DrawingContext context = visual.RenderOpen())
            {
                var typeface = new Media.Typeface(new Media.FontFamily(IconFont),
                    WPF.FontStyles.Normal, WPF.FontWeights.Normal, WPF.FontStretches.Normal);
                var text = CreateText(glyph, typeface, size * 0.94, brush);
                double x = (size - text.Width) / 2.0;
                double y = (size - text.Height) / 2.0;
                context.DrawText(text, new WPF.Point(x, y));
            }

            var bitmap = new MediaImaging.RenderTargetBitmap(size, size, 96, 96, Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        public static Drawing.Icon CreateTrayIcon(out IntPtr handle, bool muted, bool lightTaskbar)
        {
            int size = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
            if (size < 16)
            {
                size = 16;
            }
            if (size > 64)
            {
                size = 64;
            }
            handle = HiconFromBitmap(RenderStatusGlyph(size, muted, lightTaskbar));
            return Drawing.Icon.FromHandle(handle);
        }

        private static IntPtr HiconFromBitmap(MediaImaging.BitmapSource bitmap)
        {
            var encoder = new MediaImaging.PngBitmapEncoder();
            encoder.Frames.Add(MediaImaging.BitmapFrame.Create(bitmap));

            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                stream.Position = 0;
                using (var loaded = new Drawing.Bitmap(stream))
                {
                    var surface = new Drawing.Bitmap(loaded.Width, loaded.Height,
                        Drawing.Imaging.PixelFormat.Format32bppArgb);
                    using (Drawing.Graphics graphics = Drawing.Graphics.FromImage(surface))
                    {
                        graphics.CompositingMode = Drawing.Drawing2D.CompositingMode.SourceCopy;
                        graphics.DrawImage(loaded, new Drawing.Rectangle(0, 0, loaded.Width, loaded.Height));
                    }
                    IntPtr handle = surface.GetHicon();
                    surface.Dispose();
                    return handle;
                }
            }
        }

        // ------------------------------------------------------- application icon

        /// <summary>Draws the colour VolMix application artwork at the requested pixel size.</summary>
        public static MediaImaging.BitmapSource RenderAppIconBitmap(int size)
        {
            double unit = size / 32.0;
            var visual = new Media.DrawingVisual();
            using (Media.DrawingContext context = visual.RenderOpen())
            {
                double radius = 8.0 * unit;
                var gradient = new Media.LinearGradientBrush();
                gradient.StartPoint = new WPF.Point(0.1, 0);
                gradient.EndPoint = new WPF.Point(0.9, 1);
                gradient.GradientStops.Add(new Media.GradientStop(
                    (Media.Color)Media.ColorConverter.ConvertFromString("#3B8CFF"), 0));
                gradient.GradientStops.Add(new Media.GradientStop(
                    (Media.Color)Media.ColorConverter.ConvertFromString("#6A4BE8"), 1));
                gradient.Freeze();

                var clip = new Media.RectangleGeometry(new WPF.Rect(0, 0, size, size), radius, radius);
                clip.Freeze();
                context.PushClip(clip);
                context.DrawRectangle(gradient, null, new WPF.Rect(0, 0, size, size));
                context.Pop();

                Media.Brush white = Media.Brushes.White;
                var body = new Media.StreamGeometry();
                using (Media.StreamGeometryContext geometry = body.Open())
                {
                    geometry.BeginFigure(new WPF.Point(8 * unit, 13.2 * unit), true, true);
                    geometry.LineTo(new WPF.Point(11.6 * unit, 13.2 * unit), true, false);
                    geometry.LineTo(new WPF.Point(16.4 * unit, 8.4 * unit), true, false);
                    geometry.LineTo(new WPF.Point(16.4 * unit, 23.6 * unit), true, false);
                    geometry.LineTo(new WPF.Point(11.6 * unit, 18.8 * unit), true, false);
                    geometry.LineTo(new WPF.Point(8 * unit, 18.8 * unit), true, false);
                }
                body.Freeze();
                context.DrawGeometry(white, null, body);

                context.DrawEllipse(null, new Media.Pen(white, 2.0 * unit),
                    new WPF.Point(17.4 * unit, 16 * unit), 4.0 * unit, 4.0 * unit);
                context.DrawEllipse(null, new Media.Pen(white, 2.0 * unit),
                    new WPF.Point(17.4 * unit, 16 * unit), 7.4 * unit, 7.4 * unit);
            }

            var bitmap = new MediaImaging.RenderTargetBitmap(size, size, 96, 96, Media.PixelFormats.Pbgra32);
            bitmap.Render(visual);
            bitmap.Freeze();
            return bitmap;
        }

        /// <summary>
        /// Writes a multi size .ico file using the classic 32bpp DIB layout.
        /// System.Drawing.Icon.Save() downgrades the image to 4bpp and destroys
        /// the colours, so the file is assembled by hand.
        /// </summary>
        public static void WriteIconFile(string path, int[] sizes)
        {
            var images = new List<byte[]>();
            for (int i = 0; i < sizes.Length; i++)
            {
                images.Add(BuildDibEntry(sizes[i]));
            }

            using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((ushort)0);
                writer.Write((ushort)1);
                writer.Write((ushort)sizes.Length);

                int offset = 6 + (16 * sizes.Length);
                for (int i = 0; i < sizes.Length; i++)
                {
                    int size = sizes[i];
                    writer.Write((byte)(size >= 256 ? 0 : size));
                    writer.Write((byte)(size >= 256 ? 0 : size));
                    writer.Write((byte)0);
                    writer.Write((byte)0);
                    writer.Write((ushort)1);
                    writer.Write((ushort)32);
                    writer.Write((uint)images[i].Length);
                    writer.Write((uint)offset);
                    offset += images[i].Length;
                }

                for (int i = 0; i < images.Count; i++)
                {
                    writer.Write(images[i]);
                }
            }
        }

        private static byte[] BuildDibEntry(int size)
        {
            MediaImaging.BitmapSource source = RenderAppIconBitmap(size);
            var converted = new MediaImaging.FormatConvertedBitmap(source, Media.PixelFormats.Bgra32, null, 0);
            converted.Freeze();

            int stride = size * 4;
            var pixels = new byte[stride * size];
            converted.CopyPixels(pixels, stride, 0);

            int maskStride = ((size + 31) / 32) * 4;
            var mask = new byte[maskStride * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    byte alpha = pixels[(y * stride) + (x * 4) + 3];
                    if (alpha < 128)
                    {
                        mask[(y * maskStride) + (x / 8)] |= (byte)(0x80 >> (x % 8));
                    }
                }
            }

            using (var memory = new MemoryStream())
            using (var writer = new BinaryWriter(memory))
            {
                writer.Write((uint)40);
                writer.Write(size);
                writer.Write(size * 2);
                writer.Write((ushort)1);
                writer.Write((ushort)32);
                writer.Write((uint)0);
                writer.Write((uint)(stride * size));
                writer.Write(0);
                writer.Write(0);
                writer.Write((uint)0);
                writer.Write((uint)0);

                for (int y = size - 1; y >= 0; y--)
                {
                    writer.Write(pixels, y * stride, stride);
                }
                writer.Write(mask);
                writer.Flush();
                return memory.ToArray();
            }
        }

        public static void ReleaseIcon(IntPtr handle)
        {
            if (handle != IntPtr.Zero)
            {
                NativeMethods.DestroyIcon(handle);
            }
        }
    }
}

