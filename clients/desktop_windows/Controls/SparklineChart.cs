using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Media;

namespace EcosystemClient.Controls
{
    /// <summary>
    /// A lightweight, allocation-conscious area sparkline.
    ///
    /// <para>Written as a <see cref="FrameworkElement"/> with a hand-rolled
    /// <see cref="OnRender"/> pass instead of a charting library: one frozen
    /// <see cref="StreamGeometry"/> per frame, no visual tree churn, no layout passes
    /// - which is what makes it viable to redraw four of these at 4 Hz while the
    /// rest of the dashboard animates.</para>
    /// </summary>
    public sealed class SparklineChart : FrameworkElement
    {
        public static readonly DependencyProperty ValuesProperty = DependencyProperty.Register(
            nameof(Values),
            typeof(ObservableCollection<double>),
            typeof(SparklineChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnValuesPropertyChanged));

        public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(
            nameof(Stroke),
            typeof(Brush),
            typeof(SparklineChart),
            new FrameworkPropertyMetadata(Brushes.Cyan, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
            nameof(Fill),
            typeof(Brush),
            typeof(SparklineChart),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
            nameof(StrokeThickness),
            typeof(double),
            typeof(SparklineChart),
            new FrameworkPropertyMetadata(1.8d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty MaximumProperty = DependencyProperty.Register(
            nameof(Maximum),
            typeof(double),
            typeof(SparklineChart),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty FillOpacityProperty = DependencyProperty.Register(
            nameof(FillOpacity),
            typeof(double),
            typeof(SparklineChart),
            new FrameworkPropertyMetadata(0.28d, FrameworkPropertyMetadataOptions.AffectsRender));

        public ObservableCollection<double>? Values
        {
            get => (ObservableCollection<double>?)GetValue(ValuesProperty);
            set => SetValue(ValuesProperty, value);
        }

        public Brush Stroke
        {
            get => (Brush)GetValue(StrokeProperty);
            set => SetValue(StrokeProperty, value);
        }

        public Brush? Fill
        {
            get => (Brush?)GetValue(FillProperty);
            set => SetValue(FillProperty, value);
        }

        public double StrokeThickness
        {
            get => (double)GetValue(StrokeThicknessProperty);
            set => SetValue(StrokeThicknessProperty, value);
        }

        /// <summary>Fixed Y ceiling; 0 means "auto-scale to the visible window".</summary>
        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double FillOpacity
        {
            get => (double)GetValue(FillOpacityProperty);
            set => SetValue(FillOpacityProperty, value);
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            ObservableCollection<double>? values = Values;
            double width = ActualWidth;
            double height = ActualHeight;

            if (values is null || values.Count < 2 || width <= 2d || height <= 2d)
            {
                return;
            }

            double ceiling = Maximum;
            if (ceiling <= 0d)
            {
                for (int i = 0; i < values.Count; i++)
                {
                    if (values[i] > ceiling)
                    {
                        ceiling = values[i];
                    }
                }
            }

            if (ceiling <= 0d)
            {
                ceiling = 1d;
            }

            double padding = StrokeThickness + 1d;
            double plotHeight = Math.Max(1d, height - (padding * 2d));
            double stepX = width / (values.Count - 1);

            StreamGeometry geometry = new StreamGeometry();
            using (StreamGeometryContext context = geometry.Open())
            {
                double x = 0d;
                double y = padding + plotHeight - (Math.Min(values[0], ceiling) / ceiling * plotHeight);

                context.BeginFigure(new Point(x, y), isFilled: false, isClosed: false);

                for (int i = 1; i < values.Count; i++)
                {
                    x = i * stepX;
                    y = padding + plotHeight - (Math.Min(values[i], ceiling) / ceiling * plotHeight);
                    context.LineTo(new Point(x, y), isStroked: true, isSmoothJoin: true);
                }
            }

            geometry.Freeze();

            if (Fill is not null || FillOpacity > 0d)
            {
                Brush area = Fill ?? CreateAreaBrush(Stroke);
                StreamGeometry closed = new StreamGeometry();
                using (StreamGeometryContext context = closed.Open())
                {
                    double startY = padding + plotHeight - (Math.Min(values[0], ceiling) / ceiling * plotHeight);
                    context.BeginFigure(new Point(0d, height), isFilled: true, isClosed: true);
                    context.LineTo(new Point(0d, startY), isStroked: false, isSmoothJoin: false);

                    for (int i = 1; i < values.Count; i++)
                    {
                        double px = i * stepX;
                        double py = padding + plotHeight - (Math.Min(values[i], ceiling) / ceiling * plotHeight);
                        context.LineTo(new Point(px, py), isStroked: false, isSmoothJoin: false);
                    }

                    context.LineTo(new Point(width, height), isStroked: false, isSmoothJoin: false);
                }

                closed.Freeze();
                drawingContext.DrawGeometry(area, null, closed);
            }

            Pen pen = new Pen(Stroke, StrokeThickness)
            {
                LineJoin = PenLineJoin.Round,
                StartLineCap = PenLineCap.Round,
                EndLineCap = PenLineCap.Round
            };
            TryFreeze(pen);

            drawingContext.DrawGeometry(null, pen, geometry);
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

        private static void OnValuesPropertyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            SparklineChart chart = (SparklineChart)d;

            if (e.OldValue is ObservableCollection<double> previous)
            {
                previous.CollectionChanged -= chart.OnValuesCollectionChanged;
            }

            if (e.NewValue is ObservableCollection<double> current)
            {
                current.CollectionChanged += chart.OnValuesCollectionChanged;
            }

            chart.InvalidateVisual();
        }

        private void OnValuesCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => InvalidateVisual();

        private Brush CreateAreaBrush(Brush stroke)
        {
            Color color = stroke is SolidColorBrush solid ? solid.Color : Color.FromRgb(0x22, 0xD3, 0xEE);

            LinearGradientBrush gradient = new LinearGradientBrush
            {
                StartPoint = new Point(0.5d, 0d),
                EndPoint = new Point(0.5d, 1d)
            };

            gradient.GradientStops.Add(new GradientStop(WithAlpha(color, FillOpacity), 0d));
            gradient.GradientStops.Add(new GradientStop(WithAlpha(color, 0d), 1d));
            gradient.Freeze();
            return gradient;
        }

        private static Color WithAlpha(Color color, double alpha) =>
            Color.FromArgb((byte)Math.Clamp(alpha * 255d, 0d, 255d), color.R, color.G, color.B);
    }
}
