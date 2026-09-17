using System;
using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace VolMix
{
    /// <summary>
    /// Builds the Windows 11 look: control templates (slider, toggle switch,
    /// buttons, scrollbar, tooltip) plus the small animation helpers used for
    /// hover, pressed and page transitions.
    /// </summary>
    internal static class UiKit
    {
        // ------------------------------------------------------------- easing

        public static IEasingFunction FluentDecelerate()
        {
            var ease = new CubicEase();
            ease.EasingMode = EasingMode.EaseOut;
            return ease;
        }

        public static IEasingFunction FluentStandard()
        {
            var ease = new CubicEase();
            ease.EasingMode = EasingMode.EaseInOut;
            return ease;
        }

        public static IEasingFunction FluentAccelerate()
        {
            var ease = new CubicEase();
            ease.EasingMode = EasingMode.EaseIn;
            return ease;
        }

        // ---------------------------------------------------------- animations

        public static void Animate(IAnimatable target, DependencyProperty property, double to,
            int milliseconds, IEasingFunction ease)
        {
            var animation = new DoubleAnimation(to, TimeSpan.FromMilliseconds(milliseconds));
            if (ease != null)
            {
                animation.EasingFunction = ease;
            }
            target.BeginAnimation(property, animation);
        }

        public static void AnimateOpacity(UIElement element, double to, int milliseconds)
        {
            Animate(element, UIElement.OpacityProperty, to, milliseconds, FluentStandard());
        }

        public static void FadeIn(FrameworkElement element, double fromOffsetY, int milliseconds)
        {
            if (element.RenderTransform is TranslateTransform)
            {
                ((TranslateTransform)element.RenderTransform).Y = fromOffsetY;
                var move = new DoubleAnimation(0, TimeSpan.FromMilliseconds(milliseconds));
                move.EasingFunction = FluentDecelerate();
                ((TranslateTransform)element.RenderTransform).BeginAnimation(TranslateTransform.YProperty, move);
            }
            var fade = new DoubleAnimation(1, TimeSpan.FromMilliseconds(milliseconds));
            fade.EasingFunction = FluentDecelerate();
            element.BeginAnimation(UIElement.OpacityProperty, fade);
        }

        // -------------------------------------------------------------- colors

        public static Color ColorOf(Brush brush)
        {
            var solid = brush as SolidColorBrush;
            if (solid != null)
            {
                return solid.Color;
            }
            return Colors.Transparent;
        }

        public static Color WithAlpha(Brush brush, byte alpha)
        {
            Color color = ColorOf(brush);
            color.A = alpha;
            return color;
        }

        public static string Hex(Brush brush)
        {
            Color color = ColorOf(brush);
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}",
                color.A, color.R, color.G, color.B);
        }

        public static string Hex(Color color)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}{3:X2}",
                color.A, color.R, color.G, color.B);
        }

        public static SolidColorBrush Brush(Color color)
        {
            var brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        public static SolidColorBrush Brush(string hex)
        {
            return Brush((Color)ColorConverter.ConvertFromString(hex));
        }

        // --------------------------------------------------------- text/build

        public static TextBlock Text(string value, Brush foreground, double size,
            FontWeight weight, bool iconFont)
        {
            var block = new TextBlock();
            block.Text = value;
            block.Foreground = foreground;
            block.FontSize = size;
            block.FontWeight = weight;
            block.FontFamily = iconFont ? InterfaceFont.IconFamily : InterfaceFont.DefaultFamily;
            block.TextTrimming = TextTrimming.CharacterEllipsis;
            block.SnapsToDevicePixels = true;
            block.VerticalAlignment = VerticalAlignment.Center;
            return block;
        }

        public static DropShadowEffect Shadow(Brush themeColor, double blur, double depth, double opacity)
        {
            var effect = new DropShadowEffect();
            effect.Color = ColorOf(themeColor);
            effect.BlurRadius = blur;
            effect.ShadowDepth = depth;
            effect.Opacity = opacity;
            effect.Direction = 270;
            effect.RenderingBias = RenderingBias.Performance;
            return effect;
        }

        public static T Parse<T>(string xaml) where T : class
        {
            return (T)XamlReader.Parse(xaml);
        }

        // ---------------------------------------------------- template caching

        private static int _generation = -1;
        private static ControlTemplate _sliderTemplate;
        private static ControlTemplate _toggleTemplate;
        private static ControlTemplate _iconButtonTemplate;
        private static ControlTemplate _rowButtonTemplate;
        private static ControlTemplate _menuItemTemplate;
        private static Style _scrollBarStyle;
        private static Style _toolTipStyle;

        public static ControlTemplate SliderTemplate(VoltTheme theme)
        {
            Refresh(theme);
            return _sliderTemplate;
        }

        public static ControlTemplate ToggleTemplate(VoltTheme theme)
        {
            Refresh(theme);
            return _toggleTemplate;
        }

        public static ControlTemplate IconButtonTemplate(VoltTheme theme)
        {
            Refresh(theme);
            return _iconButtonTemplate;
        }

        public static ControlTemplate RowButtonTemplate(VoltTheme theme)
        {
            Refresh(theme);
            return _rowButtonTemplate;
        }

        public static ControlTemplate MenuItemTemplate(VoltTheme theme)
        {
            Refresh(theme);
            return _menuItemTemplate;
        }

        public static Style ScrollBarStyle(VoltTheme theme)
        {
            Refresh(theme);
            return _scrollBarStyle;
        }

        public static Style ToolTipStyle(VoltTheme theme)
        {
            Refresh(theme);
            return _toolTipStyle;
        }

        private static void Refresh(VoltTheme theme)
        {
            if (_generation == theme.Generation)
            {
                return;
            }

            _sliderTemplate = BuildSliderTemplate(theme);
            _toggleTemplate = BuildToggleTemplate(theme);
            _iconButtonTemplate = BuildIconButtonTemplate(theme);
            _rowButtonTemplate = BuildRowButtonTemplate(theme);
            _menuItemTemplate = BuildMenuItemTemplate(theme);
            _scrollBarStyle = BuildScrollBarStyle(theme);
            _toolTipStyle = BuildToolTipStyle(theme);
            _generation = theme.Generation;
        }

        // ------------------------------------------------------------ templates

        private const string SliderXaml = @"
<ControlTemplate TargetType=""{x:Type Slider}""
    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Grid Background=""Transparent"" Height=""32"" VerticalAlignment=""Center"">
    <Border x:Name=""PART_Remainder"" Height=""4"" CornerRadius=""2"" Background=""%REMAINDER%""
            HorizontalAlignment=""Stretch"" VerticalAlignment=""Center"" Margin=""15,0,15,0""/>
    <Border x:Name=""PART_Fill"" Width=""0"" Height=""4"" CornerRadius=""2"" Background=""%FILL%""
            HorizontalAlignment=""Left"" VerticalAlignment=""Center"" Margin=""15,0,0,0""/>
    <Track x:Name=""PART_Track"" VerticalAlignment=""Center"">
      <Track.DecreaseRepeatButton>
        <RepeatButton Command=""{x:Static Slider.DecreaseLarge}"" Focusable=""False"" IsTabStop=""False"">
          <RepeatButton.Template>
            <ControlTemplate TargetType=""{x:Type RepeatButton}"">
              <Border Background=""Transparent""/>
            </ControlTemplate>
          </RepeatButton.Template>
        </RepeatButton>
      </Track.DecreaseRepeatButton>
      <Track.Thumb>
        <Thumb Width=""30"" Height=""30"" Focusable=""False"">
          <Thumb.Template>
            <ControlTemplate TargetType=""{x:Type Thumb}"">
              <Grid Background=""Transparent"">
                <Ellipse x:Name=""Dot"" Width=""20"" Height=""20"" Fill=""%FILL%"" Stroke=""%SURFACE%""
                         StrokeThickness=""2"" HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
              </Grid>
            </ControlTemplate>
          </Thumb.Template>
        </Thumb>
      </Track.Thumb>
      <Track.IncreaseRepeatButton>
        <RepeatButton Command=""{x:Static Slider.IncreaseLarge}"" Focusable=""False"" IsTabStop=""False"">
          <RepeatButton.Template>
            <ControlTemplate TargetType=""{x:Type RepeatButton}"">
              <Border Background=""Transparent""/>
            </ControlTemplate>
          </RepeatButton.Template>
        </RepeatButton>
      </Track.IncreaseRepeatButton>
    </Track>
  </Grid>
</ControlTemplate>";

        private static ControlTemplate BuildSliderTemplate(VoltTheme theme)
        {
            string xaml = SliderXaml
                .Replace("%REMAINDER%", Hex(theme.SliderRemainder))
                .Replace("%FILL%", Hex(theme.Accent))
                .Replace("%SURFACE%", Hex(theme.CardSolid));
            return Parse<ControlTemplate>(xaml);
        }

        private const string ToggleXaml = @"
<ControlTemplate TargetType=""{x:Type CheckBox}""
    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Grid Background=""Transparent"">
    <Border x:Name=""Track"" Width=""40"" Height=""20"" CornerRadius=""10"" HorizontalAlignment=""Left""
            VerticalAlignment=""Center"" Background=""Transparent"" BorderBrush=""%STROKE%"" BorderThickness=""1""/>
    <Border x:Name=""Knob"" Width=""12"" Height=""12"" CornerRadius=""6"" HorizontalAlignment=""Left""
            VerticalAlignment=""Center"" Margin=""4,0,0,0"" Background=""%TEXT%"">
      <Border.RenderTransform>
        <TranslateTransform X=""0""/>
      </Border.RenderTransform>
    </Border>
  </Grid>
  <ControlTemplate.Triggers>
    <Trigger Property=""IsMouseOver"" Value=""True"">
      <Setter TargetName=""Knob"" Property=""Opacity"" Value=""0.8""/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

        private static ControlTemplate BuildToggleTemplate(VoltTheme theme)
        {
            string xaml = ToggleXaml
                .Replace("%STROKE%", Hex(theme.ControlStroke))
                .Replace("%TEXT%", Hex(theme.Text));
            return Parse<ControlTemplate>(xaml);
        }

        /// <summary>
        /// The on/off state of the switch is driven from code: trigger enter
        /// actions do not run when a template is applied to a control that is
        /// already checked, so the initial state would show as "off".
        /// </summary>
        public static void InitToggle(CheckBox box, VoltTheme theme, bool on)
        {
            box.ApplyTemplate();
            var track = box.Template == null ? null : box.Template.FindName("Track", box) as Border;
            var knob = box.Template == null ? null : box.Template.FindName("Knob", box) as Border;
            if (track != null)
            {
                track.Background = new SolidColorBrush(Colors.Transparent);
                track.BorderBrush = new SolidColorBrush(UiKit.ColorOf(theme.ControlStroke));
            }
            if (knob != null)
            {
                knob.Background = new SolidColorBrush(UiKit.ColorOf(theme.Text));
                // freezables coming from a parsed template are frozen; use our own
                knob.RenderTransform = new TranslateTransform(0, 0);
            }
            ApplyToggle(box, theme, on, false);
        }

        public static void ApplyToggle(CheckBox box, VoltTheme theme, bool on, bool animate)
        {
            var track = box.Template == null ? null : box.Template.FindName("Track", box) as Border;
            var knob = box.Template == null ? null : box.Template.FindName("Knob", box) as Border;
            if (track == null || knob == null)
            {
                return;
            }

            var trackFill = track.Background as SolidColorBrush;
            var trackStroke = track.BorderBrush as SolidColorBrush;
            var knobFill = knob.Background as SolidColorBrush;
            var move = knob.RenderTransform as TranslateTransform;
            if (trackFill == null || trackStroke == null || knobFill == null || move == null)
            {
                return;
            }

            Color accent = ColorOf(theme.Accent);
            Color stroke = ColorOf(theme.ControlStroke);
            Color text = ColorOf(theme.Text);

            if (!animate)
            {
                trackFill.BeginAnimation(SolidColorBrush.ColorProperty, null);
                trackStroke.BeginAnimation(SolidColorBrush.ColorProperty, null);
                knobFill.BeginAnimation(SolidColorBrush.ColorProperty, null);
                move.BeginAnimation(TranslateTransform.XProperty, null);
                trackFill.Color = on ? accent : Colors.Transparent;
                trackStroke.Color = on ? accent : stroke;
                knobFill.Color = on ? Colors.White : text;
                move.X = on ? 20 : 0;
                return;
            }

            var duration = TimeSpan.FromMilliseconds(160);
            var fill = new ColorAnimation(on ? accent : Colors.Transparent, duration);
            fill.EasingFunction = FluentStandard();
            trackFill.BeginAnimation(SolidColorBrush.ColorProperty, fill);

            var border = new ColorAnimation(on ? accent : stroke, duration);
            border.EasingFunction = FluentStandard();
            trackStroke.BeginAnimation(SolidColorBrush.ColorProperty, border);

            var knobColor = new ColorAnimation(on ? Colors.White : text, duration);
            knobColor.EasingFunction = FluentStandard();
            knobFill.BeginAnimation(SolidColorBrush.ColorProperty, knobColor);

            var slide = new DoubleAnimation(on ? 20 : 0, TimeSpan.FromMilliseconds(200));
            slide.EasingFunction = FluentDecelerate();
            move.BeginAnimation(TranslateTransform.XProperty, slide);
        }

        private const string IconButtonXaml = @"
<ControlTemplate TargetType=""{x:Type Button}""
    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Grid Background=""Transparent"">
    <Border x:Name=""Hover"" CornerRadius=""4"" Background=""%HOVER%"" Opacity=""0""/>
    <Border x:Name=""Pressed"" CornerRadius=""4"" Background=""%PRESSED%"" Opacity=""0""/>
    <ContentPresenter HorizontalAlignment=""Center"" VerticalAlignment=""Center""/>
  </Grid>
  <ControlTemplate.Triggers>
    <EventTrigger RoutedEvent=""MouseEnter"">
      <BeginStoryboard>
        <Storyboard>
          <DoubleAnimation Storyboard.TargetName=""Hover"" Storyboard.TargetProperty=""Opacity""
                           To=""1"" Duration=""0:0:0.120""/>
        </Storyboard>
      </BeginStoryboard>
    </EventTrigger>
    <EventTrigger RoutedEvent=""MouseLeave"">
      <BeginStoryboard>
        <Storyboard>
          <DoubleAnimation Storyboard.TargetName=""Hover"" Storyboard.TargetProperty=""Opacity""
                           To=""0"" Duration=""0:0:0.150""/>
        </Storyboard>
      </BeginStoryboard>
    </EventTrigger>
    <Trigger Property=""IsPressed"" Value=""True"">
      <Trigger.EnterActions>
        <BeginStoryboard>
          <Storyboard>
            <DoubleAnimation Storyboard.TargetName=""Pressed"" Storyboard.TargetProperty=""Opacity""
                             To=""1"" Duration=""0:0:0.080""/>
          </Storyboard>
        </BeginStoryboard>
      </Trigger.EnterActions>
      <Trigger.ExitActions>
        <BeginStoryboard>
          <Storyboard>
            <DoubleAnimation Storyboard.TargetName=""Pressed"" Storyboard.TargetProperty=""Opacity""
                             To=""0"" Duration=""0:0:0.180""/>
          </Storyboard>
        </BeginStoryboard>
      </Trigger.ExitActions>
    </Trigger>
    <Trigger Property=""IsEnabled"" Value=""False"">
      <Setter Property=""Opacity"" Value=""0.4""/>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

        private static ControlTemplate BuildIconButtonTemplate(VoltTheme theme)
        {
            string xaml = IconButtonXaml
                .Replace("%HOVER%", Hex(theme.SubtleFill))
                .Replace("%PRESSED%", Hex(theme.ControlFill));
            return Parse<ControlTemplate>(xaml);
        }

        private const string RowButtonXaml = @"
<ControlTemplate TargetType=""{x:Type Button}""
    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Grid Background=""Transparent"">
    <Border x:Name=""Hover"" CornerRadius=""4"" Background=""%HOVER%"" Opacity=""0""/>
    <Border x:Name=""Pressed"" CornerRadius=""4"" Background=""%PRESSED%"" Opacity=""0""/>
    <ContentPresenter HorizontalAlignment=""Stretch"" VerticalAlignment=""Stretch""/>
  </Grid>
  <ControlTemplate.Triggers>
    <EventTrigger RoutedEvent=""MouseEnter"">
      <BeginStoryboard>
        <Storyboard>
          <DoubleAnimation Storyboard.TargetName=""Hover"" Storyboard.TargetProperty=""Opacity""
                           To=""1"" Duration=""0:0:0.120""/>
        </Storyboard>
      </BeginStoryboard>
    </EventTrigger>
    <EventTrigger RoutedEvent=""MouseLeave"">
      <BeginStoryboard>
        <Storyboard>
          <DoubleAnimation Storyboard.TargetName=""Hover"" Storyboard.TargetProperty=""Opacity""
                           To=""0"" Duration=""0:0:0.150""/>
        </Storyboard>
      </BeginStoryboard>
    </EventTrigger>
    <Trigger Property=""IsPressed"" Value=""True"">
      <Trigger.EnterActions>
        <BeginStoryboard>
          <Storyboard>
            <DoubleAnimation Storyboard.TargetName=""Pressed"" Storyboard.TargetProperty=""Opacity""
                             To=""1"" Duration=""0:0:0.080""/>
          </Storyboard>
        </BeginStoryboard>
      </Trigger.EnterActions>
      <Trigger.ExitActions>
        <BeginStoryboard>
          <Storyboard>
            <DoubleAnimation Storyboard.TargetName=""Pressed"" Storyboard.TargetProperty=""Opacity""
                             To=""0"" Duration=""0:0:0.180""/>
          </Storyboard>
        </BeginStoryboard>
      </Trigger.ExitActions>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

        private static ControlTemplate BuildRowButtonTemplate(VoltTheme theme)
        {
            string xaml = RowButtonXaml
                .Replace("%HOVER%", Hex(theme.SubtleFill))
                .Replace("%PRESSED%", Hex(theme.ControlFill));
            return Parse<ControlTemplate>(xaml);
        }

        private const string MenuItemXaml = @"
<ControlTemplate TargetType=""{x:Type Button}""
    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Grid Background=""Transparent"">
    <Border x:Name=""Hover"" CornerRadius=""4"" Background=""%HOVER%"" Opacity=""0""/>
    <Border x:Name=""Pressed"" CornerRadius=""4"" Background=""%PRESSED%"" Opacity=""0""/>
    <ContentPresenter HorizontalAlignment=""Stretch"" VerticalAlignment=""Center"" Margin=""12,0,12,0""/>
  </Grid>
  <ControlTemplate.Triggers>
    <EventTrigger RoutedEvent=""MouseEnter"">
      <BeginStoryboard>
        <Storyboard>
          <DoubleAnimation Storyboard.TargetName=""Hover"" Storyboard.TargetProperty=""Opacity""
                           To=""1"" Duration=""0:0:0.100""/>
        </Storyboard>
      </BeginStoryboard>
    </EventTrigger>
    <EventTrigger RoutedEvent=""MouseLeave"">
      <BeginStoryboard>
        <Storyboard>
          <DoubleAnimation Storyboard.TargetName=""Hover"" Storyboard.TargetProperty=""Opacity""
                           To=""0"" Duration=""0:0:0.140""/>
        </Storyboard>
      </BeginStoryboard>
    </EventTrigger>
    <Trigger Property=""IsPressed"" Value=""True"">
      <Trigger.EnterActions>
        <BeginStoryboard>
          <Storyboard>
            <DoubleAnimation Storyboard.TargetName=""Pressed"" Storyboard.TargetProperty=""Opacity""
                             To=""1"" Duration=""0:0:0.070""/>
          </Storyboard>
        </BeginStoryboard>
      </Trigger.EnterActions>
      <Trigger.ExitActions>
        <BeginStoryboard>
          <Storyboard>
            <DoubleAnimation Storyboard.TargetName=""Pressed"" Storyboard.TargetProperty=""Opacity""
                             To=""0"" Duration=""0:0:0.180""/>
          </Storyboard>
        </BeginStoryboard>
      </Trigger.ExitActions>
    </Trigger>
  </ControlTemplate.Triggers>
</ControlTemplate>";

        private static ControlTemplate BuildMenuItemTemplate(VoltTheme theme)
        {
            string xaml = MenuItemXaml
                .Replace("%HOVER%", Hex(theme.SubtleFill))
                .Replace("%PRESSED%", Hex(theme.ControlFill));
            return Parse<ControlTemplate>(xaml);
        }

        private const string ScrollBarXaml = @"
<Style TargetType=""{x:Type ScrollBar}""
    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Setter Property=""Width"" Value=""12""/>
  <Setter Property=""Background"" Value=""Transparent""/>
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""{x:Type ScrollBar}"">
        <Grid Background=""Transparent"">
          <Track x:Name=""PART_Track"" IsDirectionReversed=""True"" Focusable=""False"">
            <Track.DecreaseRepeatButton>
              <RepeatButton Command=""{x:Static ScrollBar.PageUpCommand}"" Focusable=""False"" Opacity=""0"">
                <RepeatButton.Template>
                  <ControlTemplate TargetType=""{x:Type RepeatButton}"">
                    <Border Background=""Transparent""/>
                  </ControlTemplate>
                </RepeatButton.Template>
              </RepeatButton>
            </Track.DecreaseRepeatButton>
            <Track.Thumb>
              <Thumb Focusable=""False"">
                <Thumb.Template>
                  <ControlTemplate TargetType=""{x:Type Thumb}"">
                    <Grid Background=""Transparent"">
                      <Border x:Name=""Bar"" Width=""6"" CornerRadius=""3"" Background=""%THUMB%""
                              HorizontalAlignment=""Center"" VerticalAlignment=""Stretch"" Margin=""0,2,0,2"" Opacity=""0.55""/>
                    </Grid>
                    <ControlTemplate.Triggers>
                      <Trigger Property=""IsMouseOver"" Value=""True"">
                        <Setter TargetName=""Bar"" Property=""Width"" Value=""9""/>
                        <Setter TargetName=""Bar"" Property=""Opacity"" Value=""1""/>
                      </Trigger>
                      <Trigger Property=""IsDragging"" Value=""True"">
                        <Setter TargetName=""Bar"" Property=""Width"" Value=""9""/>
                        <Setter TargetName=""Bar"" Property=""Opacity"" Value=""1""/>
                      </Trigger>
                    </ControlTemplate.Triggers>
                  </ControlTemplate>
                </Thumb.Template>
              </Thumb>
            </Track.Thumb>
            <Track.IncreaseRepeatButton>
              <RepeatButton Command=""{x:Static ScrollBar.PageDownCommand}"" Focusable=""False"" Opacity=""0"">
                <RepeatButton.Template>
                  <ControlTemplate TargetType=""{x:Type RepeatButton}"">
                    <Border Background=""Transparent""/>
                  </ControlTemplate>
                </RepeatButton.Template>
              </RepeatButton>
            </Track.IncreaseRepeatButton>
          </Track>
        </Grid>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
</Style>";

        private static Style BuildScrollBarStyle(VoltTheme theme)
        {
            string xaml = ScrollBarXaml.Replace("%THUMB%", Hex(theme.TextTertiary));
            Style style = Parse<Style>(xaml);
            style.TargetType = typeof(ScrollBar);
            return style;
        }

        private const string ToolTipXaml = @"
<Style TargetType=""{x:Type ToolTip}""
    xmlns=""http://schemas.microsoft.com/winfx/2006/xaml/presentation""
    xmlns:x=""http://schemas.microsoft.com/winfx/2006/xaml"">
  <Setter Property=""Template"">
    <Setter.Value>
      <ControlTemplate TargetType=""{x:Type ToolTip}"">
        <Border CornerRadius=""4"" Background=""%CARD%"" BorderBrush=""%BORDER%"" BorderThickness=""1""
                Padding=""10,6,10,6"" Margin=""0,0,0,6"">
          <Border.Effect>
            <DropShadowEffect BlurRadius=""12"" ShadowDepth=""2"" Direction=""270"" Opacity=""0.35"" Color=""#000000""/>
          </Border.Effect>
          <ContentPresenter/>
        </Border>
      </ControlTemplate>
    </Setter.Value>
  </Setter>
  <Setter Property=""Foreground"" Value=""%TEXT%""/>
  <Setter Property=""FontFamily"" Value=""Segoe UI Variable Text, Segoe UI""/>
  <Setter Property=""FontSize"" Value=""12""/>
</Style>";

        private static Style BuildToolTipStyle(VoltTheme theme)
        {
            string xaml = ToolTipXaml
                .Replace("%CARD%", theme.Dark ? "#FF2B2B2B" : "#FFF9F9F9")
                .Replace("%BORDER%", Hex(theme.CardBorder))
                .Replace("%TEXT%", Hex(theme.Text));
            Style style = Parse<Style>(xaml);
            style.TargetType = typeof(ToolTip);
            return style;
        }
    }
}






