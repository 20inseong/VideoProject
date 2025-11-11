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
        
        public void SetTopmost(bool isTopmost)
        {
            this.Topmost = isTopmost;
        }
        
        public void SetHitTestable(bool isHitTestable)
        {
            // 메뉴가 열렸을 때는 OverlayWindow가 마우스 이벤트를 통과시킴
            this.IsHitTestVisible = isHitTestable;
        }
        
        public void SetVisible(bool isVisible)
        {
            // 메뉴가 열렸을 때는 OverlayWindow를 완전히 숨김
            if (isVisible)
            {
                this.Show();
            }
            else
            {
                this.Hide();
            }
        }
    }
}
