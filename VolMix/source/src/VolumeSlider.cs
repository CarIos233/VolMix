using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;

namespace VolMix
{
    /// <summary>
    /// Slider with an exactly aligned fill (the filled bar ends at the centre of
    /// the round thumb) and a code driven thumb size: hovering grows it slightly,
    /// pressing grows it a bit more and releasing always shrinks it back - the size
    /// can never get stuck because it is not driven by template trigger animations.
    /// </summary>
    internal sealed class VolumeSlider : Slider
    {
        public const double ThumbSlot = 30.0;
        private const double DotResting = 20.0;
        private const double DotHover = 24.0;
        private const double DotPressed = 26.0;

        private Border _fill;
        private Ellipse _dot;
        private Thumb _thumb;
        private bool _dragging;

        public VolumeSlider()
        {
            MouseEnter += delegate { ApplyDotSize(true); };
            MouseLeave += delegate { ApplyDotSize(true); };
            AddHandler(Thumb.DragStartedEvent, new DragStartedEventHandler(OnDragStarted), true);
            AddHandler(Thumb.DragCompletedEvent, new DragCompletedEventHandler(OnDragCompleted), true);
        }

        public override void OnApplyTemplate()
        {
            base.OnApplyTemplate();
            _fill = GetTemplateChild("PART_Fill") as Border;
            ResolveThumb();
            UpdateFill();
            ApplyDotSize(false);

            // the dot lives inside the Thumb template, which may be instantiated later
            if (_thumb != null)
            {
                _thumb.Loaded += delegate
                {
                    ResolveThumb();
                    UpdateFill();
                    ApplyDotSize(false);
                };
            }
        }

        protected override void OnValueChanged(double oldValue, double newValue)
        {
            base.OnValueChanged(oldValue, newValue);
            UpdateFill();
        }

        protected override void OnRenderSizeChanged(SizeChangedInfo sizeInfo)
        {
            base.OnRenderSizeChanged(sizeInfo);
            UpdateFill();
        }

        /// <summary>Forces the resting size (used after a drag ends).</summary>
        public void ResetThumbSize()
        {
            _dragging = false;
            if (_dot == null)
            {
                ResolveThumb();
            }
            ApplyDotSize(true);
        }

        private void OnDragStarted(object sender, DragStartedEventArgs e)
        {
            _dragging = true;
            ApplyDotSize(true);
        }

        private void OnDragCompleted(object sender, DragCompletedEventArgs e)
        {
            _dragging = false;
            ApplyDotSize(true);
        }

        private void ResolveThumb()
        {
            _thumb = null;
            _dot = null;
            if (Template == null)
            {
                return;
            }

            var track = Template.FindName("PART_Track", this) as Track;
            if (track == null)
            {
                return;
            }

            _thumb = track.Thumb;
            if (_thumb == null)
            {
                return;
            }
            if (!_thumb.IsLoaded)
            {
                _thumb.ApplyTemplate();
            }

            if (_thumb.Template == null)
            {
                _thumb.ApplyTemplate();
            }
            if (_thumb.Template != null)
            {
                _dot = _thumb.Template.FindName("Dot", _thumb) as Ellipse;
            }
        }

        private void ApplyDotSize(bool animate)
        {
            if (_dot == null)
            {
                // resolve lazily: the nested thumb template is applied after this slider
                ResolveThumb();
            }
            if (_dot == null)
            {
                return;
            }

            double size = _dragging ? DotPressed : (IsMouseOver ? DotHover : DotResting);
            DiagLog.Write("thumb size -> " + size + " (dragging=" + _dragging + " hover=" + IsMouseOver + ")");
            if (!animate || _dot.ActualWidth <= 0.5)
            {
                _dot.BeginAnimation(FrameworkElement.WidthProperty, null);
                _dot.BeginAnimation(FrameworkElement.HeightProperty, null);
                _dot.Width = size;
                _dot.Height = size;
                return;
            }

            double fromWidth = _dot.ActualWidth;
            double fromHeight = _dot.ActualHeight;
            _dot.Width = size;
            _dot.Height = size;

            var ease = new CubicBezierEase();
            var growWidth = new DoubleAnimation(fromWidth, size, TimeSpan.FromMilliseconds(_dragging ? 110 : 170));
            growWidth.EasingFunction = ease;
            growWidth.FillBehavior = FillBehavior.Stop;
            _dot.BeginAnimation(FrameworkElement.WidthProperty, growWidth);

            var growHeight = new DoubleAnimation(fromHeight, size, TimeSpan.FromMilliseconds(_dragging ? 110 : 170));
            growHeight.EasingFunction = ease;
            growHeight.FillBehavior = FillBehavior.Stop;
            _dot.BeginAnimation(FrameworkElement.HeightProperty, growHeight);
        }

        private void UpdateFill()
        {
            if (_fill == null)
            {
                _fill = GetTemplateChild("PART_Fill") as Border;
            }
            if (_fill == null)
            {
                return;
            }

            double width = ActualWidth;
            if (width <= 1.0)
            {
                return;
            }

            double usable = width - ThumbSlot;
            if (usable < 1.0)
            {
                usable = 1.0;
            }

            double range = Maximum - Minimum;
            double ratio = range <= 0.0 ? 0.0 : (Value - Minimum) / range;
            if (ratio < 0.0)
            {
                ratio = 0.0;
            }
            if (ratio > 1.0)
            {
                ratio = 1.0;
            }

            _fill.Width = usable * ratio;
        }
    }
}


