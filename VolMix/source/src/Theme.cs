using System;
using System.Windows.Media;

namespace VolMix
{
    /// <summary>Shared typography used across the interface.</summary>
    internal static class InterfaceFont
    {
        public static readonly FontFamily DefaultFamily =
            new FontFamily("Segoe UI Variable Text, Segoe UI");

        public static readonly FontFamily IconFamily =
            new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");

        public const double Body = 14.0;
        public const double Caption = 12.0;
        public const double Subtitle = 16.0;
    }

    /// <summary>
    /// Colour set modelled on the Windows 11 design tokens. The brushes are kept
    /// mutable so switching between light and dark updates every element that
    /// already references them.
    /// </summary>
    internal sealed class VoltTheme
    {
        /// <summary>Acrylic tint painted over the DWM backdrop (semi transparent).</summary>
        public readonly SolidColorBrush Card = new SolidColorBrush();

        /// <summary>Opaque surface used when the system backdrop is not available.</summary>
        public readonly SolidColorBrush CardSolid = new SolidColorBrush();

        public readonly SolidColorBrush CardBorder = new SolidColorBrush();
        public readonly SolidColorBrush Text = new SolidColorBrush();
        public readonly SolidColorBrush TextSecondary = new SolidColorBrush();
        public readonly SolidColorBrush TextTertiary = new SolidColorBrush();
        public readonly SolidColorBrush Accent = new SolidColorBrush();
        public readonly SolidColorBrush AccentHover = new SolidColorBrush();
        public readonly SolidColorBrush AccentPressed = new SolidColorBrush();
        public readonly SolidColorBrush Divider = new SolidColorBrush();
        public readonly SolidColorBrush SliderRemainder = new SolidColorBrush();
        public readonly SolidColorBrush SliderRim = new SolidColorBrush();
        public readonly SolidColorBrush SubtleFill = new SolidColorBrush();
        public readonly SolidColorBrush ControlFill = new SolidColorBrush();
        public readonly SolidColorBrush ControlStroke = new SolidColorBrush();
        public readonly SolidColorBrush IconBack = new SolidColorBrush();
        public readonly SolidColorBrush DangerText = new SolidColorBrush();
        public readonly SolidColorBrush Shadow = new SolidColorBrush();
        public readonly SolidColorBrush EggText = new SolidColorBrush();

        /// <summary>Base colour of the acrylic tint applied to the window.</summary>
        public Color MaterialTint;

        /// <summary>Opacity of the acrylic tint (0-255): lower means more see-through.</summary>
        public byte MaterialAlpha;

        public bool Dark { get; private set; }

        /// <summary>Incremented every time the palette changes so cached templates can be rebuilt.</summary>
        public int Generation { get; private set; }

        public VoltTheme(bool dark)
        {
            Apply(dark);
        }

        public void Apply(bool dark)
        {
            Dark = dark;
            Generation++;
            if (dark)
            {
                // the acrylic material itself provides the tint, the card only adds a glass sheen
                Set(Card, 0xFF, 0xFF, 0xFF, 0x0A);
                Set(CardSolid, 0x24, 0x24, 0x24, 0xFF);
                Set(CardBorder, 0xFF, 0xFF, 0xFF, 0x12);
                MaterialTint = Color.FromRgb(0x1B, 0x1B, 0x1B);
                MaterialAlpha = 0xB4;
                Set(Text, 0xFF, 0xFF, 0xFF, 0xFF);
                Set(TextSecondary, 0xFF, 0xFF, 0xFF, 0xB0);
                Set(TextTertiary, 0xFF, 0xFF, 0xFF, 0x78);
                Set(Accent, 0x60, 0xCD, 0xFF, 0xFF);
                Set(AccentHover, 0x7A, 0xD7, 0xFF, 0xFF);
                Set(AccentPressed, 0x4C, 0xBF, 0xF0, 0xFF);
                Set(Divider, 0xFF, 0xFF, 0xFF, 0x14);
                Set(SliderRemainder, 0xFF, 0xFF, 0xFF, 0x5C);
                Set(SliderRim, 0x00, 0x00, 0x00, 0x33);
                Set(SubtleFill, 0xFF, 0xFF, 0xFF, 0x0F);
                Set(ControlFill, 0xFF, 0xFF, 0xFF, 0x17);
                Set(ControlStroke, 0xFF, 0xFF, 0xFF, 0x24);
                Set(IconBack, 0x3E, 0x63, 0x8C, 0xFF);
                Set(DangerText, 0xFF, 0x99, 0x99, 0xFF);
                Set(Shadow, 0x00, 0x00, 0x00, 0x9E);
                Set(EggText, 0xD8, 0xA1, 0x3C, 0xFF);
            }
            else
            {
                Set(Card, 0xFF, 0xFF, 0xFF, 0x14);
                Set(CardSolid, 0xF4, 0xF4, 0xF4, 0xFF);
                Set(CardBorder, 0x00, 0x00, 0x00, 0x12);
                MaterialTint = Color.FromRgb(0xF2, 0xF2, 0xF2);
                MaterialAlpha = 0xB0;
                Set(Text, 0x1A, 0x1A, 0x1A, 0xFF);
                Set(TextSecondary, 0x1A, 0x1A, 0x1A, 0xA0);
                Set(TextTertiary, 0x1A, 0x1A, 0x1A, 0x70);
                Set(Accent, 0x00, 0x67, 0xC0, 0xFF);
                Set(AccentHover, 0x00, 0x78, 0xD4, 0xFF);
                Set(AccentPressed, 0x00, 0x54, 0x9E, 0xFF);
                Set(Divider, 0x00, 0x00, 0x00, 0x14);
                Set(SliderRemainder, 0x00, 0x00, 0x00, 0x58);
                Set(SliderRim, 0x00, 0x00, 0x00, 0x1F);
                Set(SubtleFill, 0x00, 0x00, 0x00, 0x0F);
                Set(ControlFill, 0xFF, 0xFF, 0xFF, 0xFF);
                Set(ControlStroke, 0x00, 0x00, 0x00, 0x24);
                Set(IconBack, 0x00, 0x78, 0xD4, 0xFF);
                Set(DangerText, 0xC4, 0x2B, 0x1C, 0xFF);
                Set(Shadow, 0x00, 0x00, 0x00, 0x54);
                Set(EggText, 0xB0, 0x74, 0x12, 0xFF);
            }
        }

        private static void Set(SolidColorBrush brush, byte r, byte g, byte b, byte a)
        {
            brush.Color = Color.FromArgb(a, r, g, b);
        }
    }

    internal static class VoltThemeFactory
    {
        private static VoltTheme _shared;

        /// <summary>The process wide theme instance (dark/light aware).</summary>
        public static VoltTheme Shared
        {
            get
            {
                if (_shared == null)
                {
                    _shared = new VoltTheme(IsSystemDark());
                }
                return _shared;
            }
        }

        /// <summary>Kept for compatibility with icon rendering helpers.</summary>
        public static VoltTheme FromSystem()
        {
            return Shared;
        }

        /// <summary>true when the system uses dark mode for applications.</summary>
        public static bool IsSystemDark()
        {
            return ReadDword(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "AppsUseLightTheme", 1) == 0;
        }

        /// <summary>true when the taskbar / notification area is light (dark icons needed).</summary>
        public static bool IsLightTaskbar()
        {
            return ReadDword(
                @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
                "SystemUsesLightTheme", 0) != 0;
        }

        private static int ReadDword(string subKey, string name, int fallback)
        {
            try
            {
                object value = Microsoft.Win32.Registry.GetValue(
                    @"HKEY_CURRENT_USER\" + subKey, name, fallback);
                if (value != null)
                {
                    return Convert.ToInt32(value);
                }
            }
            catch
            {
            }
            return fallback;
        }
    }
}

