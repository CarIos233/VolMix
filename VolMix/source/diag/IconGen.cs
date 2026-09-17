using System;
using System.IO;
using VolMix;

namespace VolMixDiag
{
    /// <summary>Renders the application icon (used as the exe icon) to an .ico file.</summary>
    internal static class IconGen
    {
        [STAThread]
        public static void Main(string[] args)
        {
            if (args.Length < 1)
            {
                Console.WriteLine("usage: icongen <output.ico> [preview.png]");
                Environment.Exit(2);
            }

            var app = new System.Windows.Application();

            string folder = Path.GetDirectoryName(Path.GetFullPath(args[0]));
            if (!Directory.Exists(folder))
            {
                Directory.CreateDirectory(folder);
            }

            IconHelper.WriteIconFile(args[0], new[] { 16, 32, 48 });
            Console.WriteLine("icon written: " + args[0] + " (" + new FileInfo(args[0]).Length + " bytes)");

            if (args.Length > 1)
            {
                SavePreview(32, args[1]);
                Console.WriteLine("preview written: " + args[1]);
            }
        }

        private static void SavePreview(int size, string path)
        {
            System.Windows.Media.Imaging.BitmapSource source = IconHelper.RenderAppIconBitmap(size);
            var encoder = new System.Windows.Media.Imaging.PngBitmapEncoder();
            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(source));
            using (FileStream png = File.Create(path))
            {
                encoder.Save(png);
            }
        }
    }
}

