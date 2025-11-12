using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace VideoEditor.Views.Controls
{
    public partial class WaveformControl : UserControl
    {
        public static readonly DependencyProperty WaveformDataProperty = DependencyProperty.Register(
            nameof(WaveformData),
            typeof(IEnumerable<Point>),
            typeof(WaveformControl),
            new PropertyMetadata(null, OnWaveformDataChanged));

        public IEnumerable<Point> WaveformData
        {
            get => (IEnumerable<Point>)GetValue(WaveformDataProperty);
            set => SetValue(WaveformDataProperty, value);
        }

        public WaveformControl()
        { 
            InitializeComponent();
            SizeChanged += (s, e) => Redraw();
        }

        private static void OnWaveformDataChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is WaveformControl control) 
            {
                control.Redraw();
            }
        }

        private void Redraw()
        {
            WaveformCanvas.Children.Clear();
            if (WaveformData == null) return;

            var width = WaveformCanvas.ActualWidth;
            var height = WaveformCanvas.ActualHeight;
            if (width <= 0 || height <= 0) return;

            var pathFigure = new PathFigure();
            pathFigure.StartPoint = new Point(0, height / 2);

            var topSegment = new PolyLineSegment();
            var bottomSegment = new PolyLineSegment();
            var linePoints = new List<Point>(WaveformData);

            if (linePoints.Count == 0) return;

            for (int i = 0; i < linePoints.Count; i++)
            {
                double x = (double)i / linePoints.Count * width;
                double max = linePoints[i].Y;
                double min = linePoints[i].X;

                topSegment.Points.Add(new Point(x, (1 - max) * height / 2));
                bottomSegment.Points.Add(new Point(x, (1 - min) * height / 2));
            }

            var reversedBottomPoints = new List<Point>(bottomSegment.Points);
            reversedBottomPoints.Reverse();

            pathFigure.Segments.Add(topSegment);
            pathFigure.Segments.Add(new LineSegment(new Point(width, height / 2), true));
            foreach (var p in reversedBottomPoints)
            {
                pathFigure.Segments.Add(new LineSegment(p, true));
            }

            var pathGeometry = new PathGeometry(new[] { pathFigure });
            var path = new Path
            {
                Fill = new SolidColorBrush(Colors.LightSkyBlue),
                Data = pathGeometry
            };

            WaveformCanvas.Children.Add(path);
        }
    }
}
