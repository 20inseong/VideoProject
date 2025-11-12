using System.Windows.Controls;
using System.Windows;
using System.Windows.Media;

namespace VideoEditor.Views
{
    public partial class VideoPreviewOverlay : UserControl
    {
        public VideoPreviewOverlay()
        {
            InitializeComponent();
            this.Loaded += VideoPreviewOverlay_Loaded;
            this.SizeChanged += VideoPreviewOverlay_SizeChanged;
        }

        private void VideoPreviewOverlay_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyClipping();
        }

        private void VideoPreviewOverlay_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            ApplyClipping();
        }

        private void ApplyClipping()
        {
            if (this.ActualWidth > 0 && this.ActualHeight > 0)
            {
                var clipGeometry = new RectangleGeometry(new Rect(0, 0, this.ActualWidth, this.ActualHeight));
                this.Clip = clipGeometry;
            }
        }
    }
}