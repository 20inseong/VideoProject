using System.Windows;
using System.Windows.Media;
using System.Windows.Interop;
using System;
using System.Runtime.InteropServices;

namespace VideoEditor.Views
{
    public partial class OverlayWindow : Window
    {
        private const int WS_EX_NOACTIVATE = 0x08000000;
        private const int WS_EX_TRANSPARENT = 0x00000020;
        private const int GWL_EXSTYLE = -20;

        [DllImport("user32.dll")]
        private static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        private static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        private bool _isInitialized = false;

        public OverlayWindow()
        {
            InitializeComponent();
            
            // Set WS_EX_NOACTIVATE when window is loaded
            this.SourceInitialized += OverlayWindow_SourceInitialized;
            this.IsVisibleChanged += OverlayWindow_IsVisibleChanged;
        }

        private void OverlayWindow_SourceInitialized(object sender, EventArgs e)
        {
            if (_isInitialized) return;
            _isInitialized = true;
            
            var hwnd = new WindowInteropHelper(this).Handle;
            
            int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
            SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
        }

        private void OverlayWindow_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (this.IsVisible && _isInitialized)
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                if (hwnd != IntPtr.Zero)
                {
                    int exStyle = GetWindowLong(hwnd, GWL_EXSTYLE);
                    if ((exStyle & WS_EX_NOACTIVATE) == 0)
                    {
                        SetWindowLong(hwnd, GWL_EXSTYLE, exStyle | WS_EX_NOACTIVATE);
                    }
                }
            }
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
