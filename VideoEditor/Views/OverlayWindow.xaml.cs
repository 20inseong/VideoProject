using System.Windows;
using System.Windows.Media;

namespace VideoEditor.Views
{
    public partial class OverlayWindow : Window
    {
        public OverlayWindow()
        {
            InitializeComponent();
        }
        
        public void SetScale(double scaleX, double scaleY)
        {
            if (OverlayScaleTransform != null)
            {
                OverlayScaleTransform.ScaleX = scaleX;
                OverlayScaleTransform.ScaleY = scaleY;
            }
        }
    }
}
