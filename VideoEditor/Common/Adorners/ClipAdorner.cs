using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using VideoEditor.Models;

namespace VideoEditor.Common.Adorners
{
    public class ClipAdorner : Adorner
    {
        private Thumb _topLeft, _topRight, _bottomLeft, _bottomRight, _middle;
        private VisualCollection _visuals;
        private TimelineClipBase _clip;
        private Point _dragStartPoint;
        private double _initialX, _initialY;
        private DateTime _lastClipUpdateTime = DateTime.MinValue;
        private const int CLIP_UPDATE_THROTTLE_MS = 16; // 60fps = ~16ms
        private double _aspectRatio = 1.0;

        public ClipAdorner(UIElement adornedElement, TimelineClipBase clip) : base(adornedElement)
        {
            _clip = clip;
            _visuals = new VisualCollection(this);
            
            // Calculate aspect ratio from source dimensions
            if (_clip is VideoClip videoClip && videoClip.SourceWidth > 0 && videoClip.SourceHeight > 0)
            {
                _aspectRatio = (double)videoClip.SourceWidth / videoClip.SourceHeight;
            }
            else if (_clip is ImageClip imageClip && imageClip.SourceWidth > 0 && imageClip.SourceHeight > 0)
            {
                _aspectRatio = (double)imageClip.SourceWidth / imageClip.SourceHeight;
            }
            else if (_clip.RenderWidth > 0 && _clip.RenderHeight > 0)
            {
                _aspectRatio = _clip.RenderWidth / _clip.RenderHeight;
            }
            
            BuildAdornerHandles();
            this.DataContext = _clip; // Set DataContext for binding
        }

        private void BuildAdornerHandles()
        {
            _topLeft = GetResizeThumb(Cursors.SizeNWSE, HorizontalAlignment.Left, VerticalAlignment.Top);
            _topRight = GetResizeThumb(Cursors.SizeNESW, HorizontalAlignment.Right, VerticalAlignment.Top);
            _bottomLeft = GetResizeThumb(Cursors.SizeNESW, HorizontalAlignment.Left, VerticalAlignment.Bottom);
            _bottomRight = GetResizeThumb(Cursors.SizeNWSE, HorizontalAlignment.Right, VerticalAlignment.Bottom);
            _middle = GetMoveThumb(Cursors.SizeAll);

            // Add drag started/completed handlers for video clip overlay hiding
            if (_clip is VideoClip)
            {
                _topLeft.DragStarted += Resize_DragStarted;
                _topRight.DragStarted += Resize_DragStarted;
                _bottomLeft.DragStarted += Resize_DragStarted;
                _bottomRight.DragStarted += Resize_DragStarted;

                _topLeft.DragCompleted += Resize_DragCompleted;
                _topRight.DragCompleted += Resize_DragCompleted;
                _bottomLeft.DragCompleted += Resize_DragCompleted;
                _bottomRight.DragCompleted += Resize_DragCompleted;
            }

            _topLeft.DragDelta += TopLeft_DragDelta;
            _topRight.DragDelta += TopRight_DragDelta;
            _bottomLeft.DragDelta += BottomLeft_DragDelta;
            _bottomRight.DragDelta += BottomRight_DragDelta;
            
            _middle.DragStarted += Middle_DragStarted;
            _middle.DragDelta += Middle_DragDelta;
            _middle.DragCompleted += Middle_DragCompleted;

            _visuals.Add(_middle);
            _visuals.Add(_topLeft);
            _visuals.Add(_topRight);
            _visuals.Add(_bottomLeft);
            _visuals.Add(_bottomRight);
        }

        private void Resize_DragStarted(object sender, DragStartedEventArgs e)
        {
            if (_clip is VideoClip)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                    {
                        mainViewModel.StartVideoClipPreviewDrag();
                    }
                });
            }
        }

        private void Resize_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            if (_clip is VideoClip)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                    {
                        mainViewModel.EndVideoClipPreviewDrag();
                    }
                });
            }
        }

        private Thumb GetResizeThumb(Cursor cursor, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment)
        {
            var thumb = new Thumb
            {
                Width = 10,
                Height = 10,
                BorderBrush = Brushes.Yellow, // Make border yellow for visibility
                BorderThickness = new Thickness(1),
                Cursor = cursor,
                HorizontalAlignment = horizontalAlignment,
                VerticalAlignment = verticalAlignment
            };

            // Apply the custom style
            if (Application.Current.Resources.Contains("TransparentThumbStyle"))
            {
                thumb.Style = (Style)Application.Current.Resources["TransparentThumbStyle"];
            }
            return thumb;
        }

        private Thumb GetMoveThumb(Cursor cursor)
        {
            var thumb = new Thumb
            {
                Cursor = cursor,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
                Background = Brushes.Transparent
            };

            // Apply the custom style
            if (Application.Current.Resources.Contains("TransparentThumbStyle"))
            {
                thumb.Style = (Style)Application.Current.Resources["TransparentThumbStyle"];
            }
            return thumb;
        }

        private void TopLeft_DragDelta(object sender, DragDeltaEventArgs e)
        {
            // TextClip은 비율 무시, 자유롭게 크기 조절
            if (_clip is TextClip)
            {
                double newWidth = _clip.RenderWidth - e.HorizontalChange;
                double newHeight = _clip.RenderHeight - e.VerticalChange;

                if (newWidth > 20 && newHeight > 20) // 최소 크기
                {
                    double widthChange = _clip.RenderWidth - newWidth;
                    double heightChange = _clip.RenderHeight - newHeight;
                    
                    _clip.X += widthChange;
                    _clip.Y += heightChange;
                    _clip.RenderWidth = newWidth;
                    _clip.RenderHeight = newHeight;
                    
                    ForceClipUpdate();
                }
            }
            else
            {
                // VideoClip, ImageClip은 비율 유지
                double newWidth = _clip.RenderWidth - e.HorizontalChange;
                double newHeight = newWidth / _aspectRatio;

                if (newWidth > 0 && newHeight > 0)
                {
                    double widthChange = _clip.RenderWidth - newWidth;
                    double heightChange = _clip.RenderHeight - newHeight;
                    
                    _clip.X += widthChange;
                    _clip.Y += heightChange;
                    _clip.RenderWidth = newWidth;
                    _clip.RenderHeight = newHeight;
                    
                    // ImageClip인 경우 CustomWidth/Height도 업데이트
                    if (_clip is ImageClip imageClip)
                    {
                        imageClip.UpdateCustomSizeFromRenderSize();
                    }
                    
                    ForceClipUpdate();
                }
            }
        }

        private void TopRight_DragDelta(object sender, DragDeltaEventArgs e)
        {
            // TextClip은 비율 무시, 자유롭게 크기 조절
            if (_clip is TextClip)
            {
                double newWidth = _clip.RenderWidth + e.HorizontalChange;
                double newHeight = _clip.RenderHeight - e.VerticalChange;

                if (newWidth > 20 && newHeight > 20) // 최소 크기
                {
                    double heightChange = _clip.RenderHeight - newHeight;
                    
                    _clip.Y += heightChange;
                    _clip.RenderWidth = newWidth;
                    _clip.RenderHeight = newHeight;
                    
                    ForceClipUpdate();
                }
            }
            else
            {
                // VideoClip, ImageClip은 비율 유지
                double newWidth = _clip.RenderWidth + e.HorizontalChange;
                double newHeight = newWidth / _aspectRatio;

                if (newWidth > 0 && newHeight > 0)
                {
                    double heightChange = _clip.RenderHeight - newHeight;
                    
                    _clip.Y += heightChange;
                    _clip.RenderWidth = newWidth;
                    _clip.RenderHeight = newHeight;
                    
                    // ImageClip인 경우 CustomWidth/Height도 업데이트
                    if (_clip is ImageClip imageClip)
                    {
                        imageClip.UpdateCustomSizeFromRenderSize();
                    }
                    
                    ForceClipUpdate();
                }
            }
        }

        private void BottomLeft_DragDelta(object sender, DragDeltaEventArgs e)
        {
            // TextClip은 비율 무시, 자유롭게 크기 조절
            if (_clip is TextClip)
            {
                double newWidth = _clip.RenderWidth - e.HorizontalChange;
                double newHeight = _clip.RenderHeight + e.VerticalChange;

                if (newWidth > 20 && newHeight > 20) // 최소 크기
                {
                    double widthChange = _clip.RenderWidth - newWidth;
                    
                    _clip.X += widthChange;
                    _clip.RenderWidth = newWidth;
                    _clip.RenderHeight = newHeight;
                    
                    ForceClipUpdate();
                }
            }
            else
            {
                // VideoClip, ImageClip은 비율 유지
                double newWidth = _clip.RenderWidth - e.HorizontalChange;
                double newHeight = newWidth / _aspectRatio;

                if (newWidth > 0 && newHeight > 0)
                {
                    double widthChange = _clip.RenderWidth - newWidth;
                    
                    _clip.X += widthChange;
                    _clip.RenderWidth = newWidth;
                    _clip.RenderHeight = newHeight;
                    
                    // ImageClip인 경우 CustomWidth/Height도 업데이트
                    if (_clip is ImageClip imageClip)
                    {
                        imageClip.UpdateCustomSizeFromRenderSize();
                    }
                    
                    ForceClipUpdate();
                }
            }
        }

        private void BottomRight_DragDelta(object sender, DragDeltaEventArgs e)
        {
            // TextClip은 비율 무시, 자유롭게 크기 조절
            if (_clip is TextClip)
            {
                double newWidth = _clip.RenderWidth + e.HorizontalChange;
                double newHeight = _clip.RenderHeight + e.VerticalChange;

                if (newWidth > 20 && newHeight > 20) // 최소 크기
                {
                    _clip.RenderWidth = newWidth;
                    _clip.RenderHeight = newHeight;
                    
                    ForceClipUpdate();
                }
            }
            else
            {
                // VideoClip, ImageClip은 비율 유지
                double newWidth = _clip.RenderWidth + e.HorizontalChange;
                double newHeight = newWidth / _aspectRatio;

                if (newWidth > 0 && newHeight > 0)
                {
                    _clip.RenderWidth = newWidth;
                    _clip.RenderHeight = newHeight;
                    
                    // ImageClip인 경우 CustomWidth/Height도 업데이트
                    if (_clip is ImageClip imageClip)
                    {
                        imageClip.UpdateCustomSizeFromRenderSize();
                    }
                    
                    ForceClipUpdate();
                }
            }
        }
        
        private void ForceClipUpdate()
        {
            // Force immediate clipping update for video clips during drag
            if (_clip is VideoClip)
            {
                // Throttle updates to max 60fps (16ms interval)
                var now = DateTime.Now;
                if ((now - _lastClipUpdateTime).TotalMilliseconds < CLIP_UPDATE_THROTTLE_MS)
                {
                    return; // Skip this update, too soon
                }
                
                _lastClipUpdateTime = now;
                
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (Application.Current.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.ForceClipVideoViews();
                    }
                }, System.Windows.Threading.DispatcherPriority.Render); // Render priority instead of Send
            }
        }

        private void Middle_DragStarted(object sender, DragStartedEventArgs e)
        {
            // Store initial position when drag starts
            _initialX = _clip.X;
            _initialY = _clip.Y;

            // If this is a VideoClip, notify MainViewModel to hide WPF overlays
            if (_clip is VideoClip)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                    {
                        mainViewModel.StartVideoClipPreviewDrag();
                    }
                });
            }
        }

        private void Middle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            // Update position based on drag delta
            _clip.X += e.HorizontalChange;
            _clip.Y += e.VerticalChange;
            
            ForceClipUpdate();
        }

        private void Middle_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            // If this is a VideoClip, notify MainViewModel to restore WPF overlays
            if (_clip is VideoClip)
            {
                Application.Current?.Dispatcher.Invoke(() =>
                {
                    if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                    {
                        mainViewModel.EndVideoClipPreviewDrag();
                    }
                });
            }

            // Force final update when drag completes to ensure accurate clipping
            if (_clip is VideoClip)
            {
                _lastClipUpdateTime = DateTime.MinValue; // Reset throttle
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (Application.Current.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.ForceClipVideoViews();
                    }
                }, System.Windows.Threading.DispatcherPriority.Render);
            }
        }

        protected override Size MeasureOverride(Size constraint)
        {
            // Ensure the adorned element is measured
            var desiredSize = base.MeasureOverride(constraint);

            // Measure the thumbs
            foreach (UIElement thumb in _visuals)
            {
                thumb.Measure(constraint);
            }
            return desiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            // Arrange the adorned element
            base.ArrangeOverride(finalSize);

            // Arrange the thumbs based on the adorned element's actual bounds
            // The adorner uses local coordinates relative to the adorned element
            // So we use 0,0 as the top-left corner
            double left = 0;
            double top = 0;
            double width = _clip.RenderWidth;
            double height = _clip.RenderHeight;

            // Top-Left
            _topLeft.Arrange(new Rect(left, top, _topLeft.DesiredSize.Width, _topLeft.DesiredSize.Height));
            // Top-Right
            _topRight.Arrange(new Rect(left + width - _topRight.DesiredSize.Width, top, _topRight.DesiredSize.Width, _topRight.DesiredSize.Height));
            // Bottom-Left
            _bottomLeft.Arrange(new Rect(left, top + height - _bottomLeft.DesiredSize.Height, _bottomLeft.DesiredSize.Width, _bottomLeft.DesiredSize.Height));
            // Bottom-Right
            _bottomRight.Arrange(new Rect(left + width - _bottomRight.DesiredSize.Width, top + height - _bottomRight.DesiredSize.Height, _bottomRight.DesiredSize.Width, _bottomRight.DesiredSize.Height));
            // Middle (for dragging)
            _middle.Arrange(new Rect(left, top, width, height));

            return finalSize;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
            // Draw a transparent rectangle to ensure the adorner itself doesn't have an opaque background
            drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
            base.OnRender(drawingContext);
        }

        protected override int VisualChildrenCount => _visuals.Count;

        protected override Visual GetVisualChild(int index)
        {
            return _visuals[index];
        }
    }
}