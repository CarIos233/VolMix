using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace VolMix
{
    /// <summary>
    /// Windows 11 "Fluent" motion curve: cubic bezier (0.1, 0.9, 0.2, 1.0).
    /// Used for the open/close animation of the flyout.
    /// </summary>
    internal sealed class CubicBezierEase : EasingFunctionBase
    {
        private readonly double _x1;
        private readonly double _y1;
        private readonly double _x2;
        private readonly double _y2;

        public CubicBezierEase()
        {
            _x1 = 0.10;
            _y1 = 0.90;
            _x2 = 0.20;
            _y2 = 1.00;
            EasingMode = EasingMode.EaseIn;
        }

        public CubicBezierEase(double x1, double y1, double x2, double y2)
        {
            _x1 = x1;
            _y1 = y1;
            _x2 = x2;
            _y2 = y2;
            EasingMode = EasingMode.EaseIn;
        }

        protected override double EaseInCore(double normalizedTime)
        {
            double u = normalizedTime;
            for (int i = 0; i < 12; i++)
            {
                double x = Bezier(_x1, _x2, u) - normalizedTime;
                if (Math.Abs(x) < 0.0000001)
                {
                    break;
                }
                double slope = Derivative(_x1, _x2, u);
                if (Math.Abs(slope) < 0.0000001)
                {
                    break;
                }
                u = u - (x / slope);
                if (u < 0.0)
                {
                    u = 0.0;
                    break;
                }
                if (u > 1.0)
                {
                    u = 1.0;
                    break;
                }
            }
            return Bezier(_y1, _y2, u);
        }

        protected override Freezable CreateInstanceCore()
        {
            return new CubicBezierEase(_x1, _y1, _x2, _y2);
        }

        private static double Bezier(double p1, double p2, double u)
        {
            double v = 1.0 - u;
            return (3.0 * v * v * u * p1) + (3.0 * v * u * u * p2) + (u * u * u);
        }

        private static double Derivative(double p1, double p2, double u)
        {
            double v = 1.0 - u;
            return (3.0 * v * v * p1) + (6.0 * v * u * (p2 - p1)) + (3.0 * u * u * (1.0 - p2));
        }
    }

    /// <summary>Win11 style radio button and the settings page easter egg.</summary>
    internal static class FluentKit
    {
        private static ControlTemplate _radioTemplate;
        private static int _radioGeneration = -1;

        // ------------------------------------------------------------ radio button

        private const string RadioXaml = @"
<ControlTemplate TargetType=""{x:Type CheckBox}""
    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Grid Background=""Transparent"" Width=""20"" Height=""20"">
    <Ellipse x:Name=""Ring"" Width=""20"" Height=""20"" Stroke=""%STROKE%"" StrokeThickness=""1""
             Fill=""Transparent"" HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
    <Ellipse x:Name=""Dot"" Width=""8"" Height=""8"" Fill=""%ACCENT%"" Opacity=""0""
             HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property=""IsMouseOver"" Value=""True"">
      <Setter TargetName=""Ring"" Property=""Opacity"" Value=""0.75""/>
    </Trigger>
    <Trigger Property=""IsEnabled"" Value=""False"">
      <Setter Property=""Opacity"" Value=""0.4""/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

        public static ControlTemplate RadioTemplate(VoltTheme theme)
        {
            if (_radioGeneration != theme.Generation)
            {
                string xaml = RadioXaml
                    .Replace("%STROKE%", UiKit.Hex(theme.ControlStroke))
                    .Replace("%ACCENT%", UiKit.Hex(theme.Accent));
                _radioTemplate = UiKit.Parse<ControlTemplate>(xaml);
                _radioGeneration = theme.Generation;
            }
            return _radioTemplate;
        }

        /// <summary>Colours of the radio are driven from code (template triggers only fire on transitions).</summary>
        public static void InitRadio(CheckBox box, VoltTheme theme, bool on)
        {
            box.ApplyTemplate();
            Ellipse ring = Find(box, "Ring");
            Ellipse dot = Find(box, "Dot");
            if (ring != null)
            {
                ring.Stroke = new SolidColorBrush(UiKit.ColorOf(theme.ControlStroke));
            }
            if (dot != null)
            {
                dot.Fill = new SolidColorBrush(UiKit.ColorOf(theme.Accent));
            }
            ApplyRadio(box, theme, on, false);
        }

        public static void ApplyRadio(CheckBox box, VoltTheme theme, bool on, bool animate)
        {
            Ellipse ring = Find(box, "Ring");
            Ellipse dot = Find(box, "Dot");
            if (ring == null || dot == null)
            {
                return;
            }

            var ringBrush = ring.Stroke as SolidColorBrush;
            var dotBrush = dot.Fill as SolidColorBrush;
            if (ringBrush == null || dotBrush == null)
            {
                return;
            }

            Color accent = UiKit.ColorOf(theme.Accent);
            Color stroke = UiKit.ColorOf(theme.ControlStroke);
            double size = on ? 10.0 : 8.0;

            if (!animate)
            {
                ringBrush.BeginAnimation(SolidColorBrush.ColorProperty, null);
                dot.BeginAnimation(UIElement.OpacityProperty, null);
                dot.BeginAnimation(FrameworkElement.WidthProperty, null);
                dot.BeginAnimation(FrameworkElement.HeightProperty, null);
                ringBrush.Color = on ? accent : stroke;
                dotBrush.Color = accent;
                dot.Opacity = on ? 1.0 : 0.0;
                dot.Width = size;
                dot.Height = size;
                return;
            }

            var ringAnimation = new ColorAnimation(on ? accent : stroke, TimeSpan.FromMilliseconds(150));
            ringAnimation.EasingFunction = new CubicBezierEase();
            ringBrush.BeginAnimation(SolidColorBrush.ColorProperty, ringAnimation);

            var opacity = new DoubleAnimation(on ? 1.0 : 0.0, TimeSpan.FromMilliseconds(150));
            opacity.EasingFunction = new CubicBezierEase();
            dot.BeginAnimation(UIElement.OpacityProperty, opacity);

            var grow = new DoubleAnimation(size, TimeSpan.FromMilliseconds(180));
            grow.EasingFunction = new CubicBezierEase();
            dot.BeginAnimation(FrameworkElement.WidthProperty, grow);
            dot.BeginAnimation(FrameworkElement.HeightProperty, grow);
        }

        private static Ellipse Find(CheckBox box, string name)
        {
            if (box.Template == null)
            {
                return null;
            }
            return box.Template.FindName(name, box) as Ellipse;
        }

        // ----------------------------------------------------------------- sparkle

        /// <summary>The little four point sparkle used for the easter egg button.</summary>
        public static FrameworkElement CreateSparkle(double size)
        {
            var path = new Path();
            path.Data = Geometry.Parse("M 9,0 L 11.3,6.3 L 17.6,8.6 L 11.3,10.9 L 9,17.2 L 6.7,10.9 L 0.4,8.6 L 6.7,6.3 Z");
            path.Stretch = Stretch.Uniform;
            path.Width = size;
            path.Height = size;
            path.SnapsToDevicePixels = true;

            var brush = new LinearGradientBrush();
            brush.StartPoint = new Point(0.0, 0.0);
            brush.EndPoint = new Point(1.0, 1.0);
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FFC53D"), 0.0));
            brush.GradientStops.Add(new GradientStop((Color)ColorConverter.ConvertFromString("#FF8AC2"), 1.0));
            brush.Freeze();
            path.Fill = brush;

            var glow = new DropShadowEffect();
            glow.Color = (Color)ColorConverter.ConvertFromString("#FFB347");
            glow.BlurRadius = 10;
            glow.ShadowDepth = 0;
            glow.Opacity = 0.6;
            path.Effect = glow;
            return path;
        }

        // -------------------------------------------------------------- easter egg

        public static void SpawnEgg(Canvas overlay)
        {
            if (overlay == null)
            {
                return;
            }

            double width = overlay.ActualWidth > 1 ? overlay.ActualWidth : 380;
            double height = overlay.ActualHeight > 1 ? overlay.ActualHeight : 320;
            var random = new Random(Environment.TickCount & 0x7FFFFFFF);
            Color color = (Color)ColorConverter.ConvertFromString("#C0872B");

            for (int i = 0; i < 7; i++)
            {
                var text = new TextBlock();
                text.Text = "\u54FC\u3001\u54FC\u3001\u554A\u554A\u554A\u554A\u554A\u554A";
                text.FontFamily = InterfaceFont.DefaultFamily;
                text.FontWeight = FontWeights.Bold;
                text.FontSize = 14 + random.Next(7);
                text.Foreground = new SolidColorBrush(color);
                text.Opacity = 0;
                text.IsHitTestVisible = false;
                text.TextAlignment = TextAlignment.Center;

                var transform = new TranslateTransform();
                text.RenderTransform = transform;

                Canvas.SetLeft(text, 20 + random.Next((int)Math.Max(1, width - 250)));
                Canvas.SetTop(text, height - 36 - random.Next(50));
                overlay.Children.Add(text);

                // float upwards for five seconds
                double rise = 150 + random.Next(90);
                var up = new DoubleAnimation(0.0, -rise, TimeSpan.FromSeconds(5));
                up.EasingFunction = new CubicBezierEase(0.25, 0.55, 0.35, 1.0);
                transform.BeginAnimation(TranslateTransform.YProperty, up);

                double sway = (random.Next(2) == 0 ? -1.0 : 1.0) * (16 + random.Next(26));
                var drift = new DoubleAnimationUsingKeyFrames();
                drift.Duration = TimeSpan.FromSeconds(5);
                drift.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
                drift.KeyFrames.Add(new LinearDoubleKeyFrame(sway, KeyTime.FromPercent(0.35)));
                drift.KeyFrames.Add(new LinearDoubleKeyFrame(-sway, KeyTime.FromPercent(0.7)));
                drift.KeyFrames.Add(new LinearDoubleKeyFrame(sway * 0.4, KeyTime.FromPercent(1.0)));
                transform.BeginAnimation(TranslateTransform.XProperty, drift);

                var fade = new DoubleAnimationUsingKeyFrames();
                fade.Duration = TimeSpan.FromSeconds(5);
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(0.0)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.08)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(1.0, KeyTime.FromPercent(0.72)));
                fade.KeyFrames.Add(new LinearDoubleKeyFrame(0.0, KeyTime.FromPercent(1.0)));
                fade.Completed += delegate { overlay.Children.Remove(text); };
                text.BeginAnimation(UIElement.OpacityProperty, fade);
            }
        }
    }
}
