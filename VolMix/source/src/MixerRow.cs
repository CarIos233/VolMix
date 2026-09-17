using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace VolMix
{
    /// <summary>
    /// A single mixer entry: app icon, name, volume slider, percentage and the
    /// mute toggle. Used for both application sessions and the device master.
    /// </summary>
    internal sealed class MixerRow
    {
        public const double RowHeight = 42.0;

        private const double IconSize = 28.0;
        private const double NameWidth = 126.0;
        private const double PercentWidth = 34.0;
        private const double MuteWidth = 28.0;

        private readonly VoltTheme _theme;
        private readonly Action<MixerRow, double> _volumeHandler;
        private readonly Action<MixerRow, bool> _muteHandler;
        private readonly TextBlock _muteGlyph;
        private readonly Border _hover;
        private readonly Grid _content = new Grid();
        private bool _suppress;

        public readonly Grid Root = new Grid();
        public readonly Image Icon = new Image();
        public readonly TextBlock NameText;
        public readonly VolumeSlider Volume = new VolumeSlider();
        public readonly TextBlock PercentText;
        public readonly Button MuteButton = new Button();

        public AudioSessionModel Model { get; set; }
        public bool Dragging { get; set; }

        public MixerRow(VoltTheme theme, AudioSessionModel model,
            Action<MixerRow, double> volumeHandler, Action<MixerRow, bool> muteHandler)
        {
            _theme = theme;
            _volumeHandler = volumeHandler;
            _muteHandler = muteHandler;
            Model = model;

            NameText = UiKit.Text("", theme.Text, InterfaceFont.Body, FontWeights.Normal, false);
            PercentText = UiKit.Text("", theme.TextSecondary, InterfaceFont.Caption, FontWeights.Normal, false);
            _muteGlyph = UiKit.Text("\uE767", theme.Text, 15, FontWeights.Normal, true);

            Root.Height = RowHeight;
            Root.Background = Brushes.Transparent;
            Root.RenderTransform = new TranslateTransform();

            _hover = new Border();
            _hover.CornerRadius = new CornerRadius(4);
            _hover.Background = theme.SubtleFill;
            _hover.Opacity = 0;
            _hover.Margin = new Thickness(-6, -1, -6, -1);
            _hover.IsHitTestVisible = false;
            Root.Children.Add(_hover);
            Root.Children.Add(_content);

            _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IconSize) });
            _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(NameWidth) });
            _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(PercentWidth) });
            _content.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(MuteWidth) });

            Icon.Width = IconSize;
            Icon.Height = IconSize;
            Icon.Stretch = Stretch.Uniform;
            Icon.VerticalAlignment = VerticalAlignment.Center;
            Icon.Clip = new RectangleGeometry(new Rect(0, 0, IconSize, IconSize), 6, 6);
            RenderOptions.SetBitmapScalingMode(Icon, BitmapScalingMode.HighQuality);
            Grid.SetColumn(Icon, 0);

            NameText.Margin = new Thickness(10, 0, 8, 0);
            Grid.SetColumn(NameText, 1);

            PercentText.HorizontalAlignment = HorizontalAlignment.Right;
            PercentText.TextAlignment = TextAlignment.Right;
            PercentText.Opacity = 0;
            PercentText.Margin = new Thickness(0, 0, 6, 0);
            Grid.SetColumn(PercentText, 3);

            MuteButton.Width = MuteWidth;
            MuteButton.Height = MuteWidth;
            MuteButton.Template = UiKit.IconButtonTemplate(theme);
            MuteButton.Cursor = Cursors.Hand;
            MuteButton.Content = _muteGlyph;
            MuteButton.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(MuteButton, 4);
            MuteButton.Click += OnMuteClicked;

            Volume.Minimum = 0;
            Volume.Maximum = 100;
            Volume.SmallChange = 2;
            Volume.LargeChange = 10;
            Volume.IsMoveToPointEnabled = true;
            Volume.Template = UiKit.SliderTemplate(theme);
            Volume.VerticalAlignment = VerticalAlignment.Center;
            Volume.Margin = new Thickness(2, 0, 6, 0);
            Volume.Focusable = false;
            Volume.IsTabStop = false;
            Volume.FocusVisualStyle = null;
            Grid.SetColumn(Volume, 2);
            Volume.ValueChanged += OnVolumeChanged;
            Volume.PreviewMouseLeftButtonDown += OnSliderDown;
            Volume.PreviewMouseLeftButtonUp += OnSliderMouseUp;
            Volume.LostMouseCapture += OnSliderLostCapture;

            _content.Children.Add(Icon);
            _content.Children.Add(NameText);
            _content.Children.Add(Volume);
            _content.Children.Add(PercentText);
            _content.Children.Add(MuteButton);

            Root.MouseEnter += delegate
            {
                UiKit.Animate(PercentText, UIElement.OpacityProperty, 1, 140, UiKit.FluentDecelerate());
                UiKit.Animate(_hover, UIElement.OpacityProperty, 1, 120, UiKit.FluentDecelerate());
            };
            Root.MouseLeave += delegate
            {
                UiKit.Animate(PercentText, UIElement.OpacityProperty, 0, 160, UiKit.FluentStandard());
                UiKit.Animate(_hover, UIElement.OpacityProperty, 0, 150, UiKit.FluentStandard());
            };
            Root.MouseDown += OnRootMouseDown;
            Root.PreviewMouseWheel += OnRowWheel;

            Apply(model);
        }

        /// <summary>Refreshes the visuals from the model without disturbing a drag.</summary>
        public void Apply(AudioSessionModel model)
        {
            Model = model;
            if (model == null)
            {
                return;
            }

            if (!Dragging)
            {
                _suppress = true;
                Volume.Value = model.Volume;
                _suppress = false;
            }

            PercentText.Text = model.Volume + "%";
            NameText.Text = model.Name;
            NameText.ToolTip = model.IsSystemSounds
                ? L.T("session.systemSounds")
                : model.Name + (string.IsNullOrEmpty(model.ImagePath) ? "" : "\n" + model.ImagePath);
            _muteGlyph.Text = model.IsMuted ? "\uE74F" : "\uE767";
            _muteGlyph.Foreground = model.IsMuted ? _theme.TextSecondary : _theme.Text;
            MuteButton.ToolTip = model.IsMuted ? L.T("mixer.unmute") : L.T("mixer.mute");
            Volume.Opacity = model.IsMuted ? 0.55 : 1.0;

            if (Icon.Source == null)
            {
                Icon.Source = IconHelper.GetIcon(model.ImagePath, model.Name, model.IsSystemSounds);
            }
        }

        /// <summary>Sets the volume from outside (keyboard, wheel) and pushes it to the device.</summary>
        public void NudgeVolume(double delta)
        {
            double target = Math.Max(0, Math.Min(100, Volume.Value + delta));
            Volume.Value = target;
        }

        private void OnVolumeChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_suppress || Model == null)
            {
                return;
            }
            PercentText.Text = ((int)Math.Round(e.NewValue)) + "%";
            UiKit.Animate(PercentText, UIElement.OpacityProperty, 1, 100, UiKit.FluentDecelerate());
            if (_volumeHandler != null)
            {
                _volumeHandler(this, e.NewValue);
            }
        }

        private void OnSliderDown(object sender, MouseButtonEventArgs e)
        {
            Dragging = true;
        }

        private void OnSliderMouseUp(object sender, MouseButtonEventArgs e)
        {
            EndDrag();
        }

        private void OnSliderLostCapture(object sender, MouseEventArgs e)
        {
            EndDrag();
        }

        private void EndDrag()
        {
            if (!Dragging)
            {
                return;
            }
            Dragging = false;
            Volume.ResetThumbSize();
            if (Model != null && _volumeHandler != null)
            {
                _volumeHandler(this, Volume.Value);
            }
        }

        private void OnMuteClicked(object sender, RoutedEventArgs e)
        {
            if (Model == null)
            {
                return;
            }
            if (_muteHandler != null)
            {
                _muteHandler(this, !Model.IsMuted);
            }
        }

        private void OnRootMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton != MouseButton.Middle || Model == null)
            {
                return;
            }
            e.Handled = true;
            if (_muteHandler != null)
            {
                _muteHandler(this, !Model.IsMuted);
            }
        }

        private void OnRowWheel(object sender, MouseWheelEventArgs e)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                return;
            }
            e.Handled = true;
            NudgeVolume(e.Delta > 0 ? 2 : -2);
        }
    }
}





