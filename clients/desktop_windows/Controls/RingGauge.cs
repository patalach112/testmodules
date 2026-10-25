using System;
using System.Windows;
using System.Windows.Media;

namespace EcosystemClient.Controls
{
    /// <summary>
    /// A 270° arc gauge drawn directly with <see cref="DrawingContext"/>.
    ///
    /// Used for the dashboard's headline metrics (latency, packet rate, crypto
    /// ops/s). Everything is frozen after construction, so a value change costs one
    /// geometry build and no visual-tree work.
    /// </summary>
    public sealed class RingGauge : FrameworkElement
    {
        private static readonly Brush DefaultTrackBrush = CreateFrozen(Color.FromRgb(0x25, 0x25, 0x35));

        public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
            nameof(Value),
            typeof(double),
            typeof(RingGauge),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
            nameof(TrackBrush),
            typeof(Brush),
            typeof(RingGauge),
            new FrameworkPropertyMetadata(DefaultTrackBrush, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ProgressBrushProperty = DependencyProperty.Register(
            nameof(ProgressBrush),
            typeof(Brush),
            typeof(RingGauge),
            new FrameworkPropertyMetadata(Brushes.Cyan, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(
            nameof(Thickness),
            typeof(double),
            typeof(RingGauge),
            new FrameworkPropertyMetadata(6d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty GlowProperty = DependencyProperty.Register(
            nameof(Glow),
            typeof(bool),
            typeof(RingGauge),
            new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StartAngleProperty = DependencyProperty.Register(
            nameof(StartAngle),
            typeof(double),
            typeof(RingGauge),
            new FrameworkPropertyMetadata(135d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty SweepAngleProperty = DependencyProperty.Register(
            nameof(SweepAngle),
            typeof(double),
            typeof(RingGauge),
            new FrameworkPropertyMetadata(270d, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Normalised 0..1 fill.</summary>
        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public Brush TrackBrush
        {
            get => (Brush)GetValue(TrackBrushProperty);
            set => SetValue(TrackBrushProperty, value);
        }

        public Brush ProgressBrush
        {
            get => (Brush)GetValue(ProgressBrushProperty);
            set => SetValue(ProgressBrushProperty, value);
        }

        public double Thickness
        {
            get => (double)GetValue(ThicknessProperty);
            set => SetValue(ThicknessProperty, value);
        }

        /// <summary>Draws a soft, wider arc underneath the crisp one.</summary>
        public bool Glow
        {
            get => (bool)GetValue(GlowProperty);
            set => SetValue(GlowProperty, value);
        }

        public double StartAngle
        {
            get => (double)GetValue(StartAngleProperty);
            set => SetValue(StartAngleProperty, value);
        }

        public double SweepAngle
        {
            get => (double)GetValue(SweepAngleProperty);
            set => SetValue(SweepAngleProperty, value);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            double width = ActualWidth;
            double height = ActualHeight;
            double thickness = Math.Max(1d, Thickness);
            double radius = (Math.Min(width, height) / 2d) - (thickness / 2d) - (Glow ? 3d : 1d);

            if (radius <= 1d)
            {
                return;
            }

            Point center = new Point(width / 2d, height / 2d);

            Pen track = new Pen(TrackBrush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            TryFreeze(track);
            drawingContext.DrawGeometry(null, track, BuildArc(center, radius, StartAngle, SweepAngle));

            double value = Math.Clamp(Value, 0d, 1d);
            if (value <= 0.001d)
            {
                return;
            }

            double sweep = SweepAngle * value;

            if (Glow && ProgressBrush is SolidColorBrush solid)
            {
                Color glowColor = Color.FromArgb(70, solid.Color.R, solid.Color.G, solid.Color.B);
                Pen glow = new Pen(new SolidColorBrush(glowColor), thickness + 5d)
                {
                    StartLineCap = PenLineCap.Round,
                    EndLineCap = PenLineCap.Round
                };
                TryFreeze(glow);
                drawingContext.DrawGeometry(null, glow, BuildArc(center, radius, StartAngle, sweep));
            }

            Pen progress = new Pen(ProgressBrush, thickness)
            {
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            TryFreeze(progress);
            drawingContext.DrawGeometry(null, progress, BuildArc(center, radius, StartAngle, sweep));
        }

        private static Geometry BuildArc(Point center, double radius, double startAngle, double sweepAngle)
        {
            StreamGeometry geometry = new StreamGeometry();

            using (StreamGeometryContext context = geometry.Open())
            {
                Point start = PointOnCircle(center, radius, startAngle);
                Point end = PointOnCircle(center, radius, startAngle + sweepAngle);

                context.BeginFigure(start, isFilled: false, isClosed: false);
                context.ArcTo(
                    end,
                    new Size(radius, radius),
                    rotationAngle: 0d,
                    isLargeArc: Math.Abs(sweepAngle) > 180d,
                    sweepDirection: SweepDirection.Clockwise,
                    isStroked: true,
                    isSmoothJoin: true);
            }

            geometry.Freeze();
            return geometry;
        }

        /// <summary>
        /// Freezes when possible. A brush supplied by a binding may already be frozen,
        /// animated, or otherwise unfreezable - none of which should break rendering.
        /// </summary>
        private static void TryFreeze(Freezable? value)
        {
            if (value is null || value.IsFrozen)
            {
                return;
            }

            try
            {
                value.Freeze();
            }
            catch (InvalidOperationException)
            {
                // Animated or expression-bound: leave it unfrozen.
            }
        }

        private static Brush CreateFrozen(Color color)
        {
            SolidColorBrush brush = new SolidColorBrush(color);
            brush.Freeze();
            return brush;
        }

        private static Point PointOnCircle(Point center, double radius, double angleDegrees)
        {
            double radians = angleDegrees * Math.PI / 180d;
            return new Point(
                center.X + (radius * Math.Cos(radians)),
                center.Y + (radius * Math.Sin(radians)));
        }
    }
}
