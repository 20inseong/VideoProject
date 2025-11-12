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
            this.DataContext = _clip;
        }

        private void BuildAdornerHandles()
        {
            _topLeft = GetResizeThumb(Cursors.SizeNWSE, HorizontalAlignment.Left, VerticalAlignment.Top);
            _topRight = GetResizeThumb(Cursors.SizeNESW, HorizontalAlignment.Right, VerticalAlignment.Top);
            _bottomLeft = GetResizeThumb(Cursors.SizeNESW, HorizontalAlignment.Left, VerticalAlignment.Bottom);
            _bottomRight = GetResizeThumb(Cursors.SizeNWSE, HorizontalAlignment.Right, VerticalAlignment.Bottom);
            _middle = GetMoveThumb(Cursors.SizeAll);

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
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                {
                    mainViewModel.StopPlayback();
                    
                    if (_clip is VideoClip)
                    {
                        mainViewModel.StartVideoClipPreviewDrag();
                    }
                }
            });
        }

        private void Resize_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                {
                    if (_clip is VideoClip)
                    {
                        mainViewModel.EndVideoClipPreviewDrag();
                    }
                    
                    mainViewModel.ResumePlaybackIfNeeded();
                }
            });
        }

        private Thumb GetResizeThumb(Cursor cursor, HorizontalAlignment horizontalAlignment, VerticalAlignment verticalAlignment)
        {
            var thumb = new Thumb
            {
                Width = 10,
                Height = 10,
                BorderBrush = Brushes.Yellow,
                BorderThickness = new Thickness(1),
                Cursor = cursor,
                HorizontalAlignment = horizontalAlignment,
                VerticalAlignment = verticalAlignment
            };

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
            if (_clip is VideoClip)
            {
                var now = DateTime.Now;
                if ((now - _lastClipUpdateTime).TotalMilliseconds < CLIP_UPDATE_THROTTLE_MS)
                {
                    return;
                }
                
                _lastClipUpdateTime = now;
                
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    if (Application.Current.MainWindow is MainWindow mainWindow)
                    {
                        mainWindow.ForceClipVideoViews();
                    }
                }, System.Windows.Threading.DispatcherPriority.Render);
            }
        }

        private void Middle_DragStarted(object sender, DragStartedEventArgs e)
        {
            _initialX = _clip.X;
            _initialY = _clip.Y;

            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                {
                    mainViewModel.StopPlayback();
                    
                    if (_clip is VideoClip)
                    {
                        mainViewModel.StartVideoClipPreviewDrag();
                    }
                }
                
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.SetOverlayInteractionActive(true);
                    mainWindow.CancelDeactivationTimer();
                }
            });
        }

        private void Middle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newX = _clip.X + e.HorizontalChange;
            double newY = _clip.Y + e.VerticalChange;
            
            double minX = -_clip.RenderWidth + 50;
            double minY = -_clip.RenderHeight + 50;
            double maxX = 1920 - 50;
            double maxY = 1080 - 50;
            
            newX = Math.Max(minX, Math.Min(maxX, newX));
            newY = Math.Max(minY, Math.Min(maxY, newY));
            
            _clip.X = newX;
            _clip.Y = newY;
            
            ForceClipUpdate();
        }

        private void Middle_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            Application.Current?.Dispatcher.Invoke(() =>
            {
                if (Application.Current.MainWindow?.DataContext is ViewModels.MainViewModel mainViewModel)
                {
                    if (_clip is VideoClip)
                    {
                        mainViewModel.EndVideoClipPreviewDrag();
                    }
                    
                    mainViewModel.ResumePlaybackIfNeeded();
                }
                
                if (Application.Current.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.SetOverlayInteractionActive(false);
                }
            });

            if (_clip is VideoClip)
            {
                _lastClipUpdateTime = DateTime.MinValue;
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
            var desiredSize = base.MeasureOverride(constraint);

            foreach (UIElement thumb in _visuals)
            {
                thumb.Measure(constraint);
            }
            return desiredSize;
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            base.ArrangeOverride(finalSize);

            double left = 0;
            double top = 0;
            double width = _clip.RenderWidth;
            double height = _clip.RenderHeight;

            _topLeft.Arrange(new Rect(left, top, _topLeft.DesiredSize.Width, _topLeft.DesiredSize.Height));
            _topRight.Arrange(new Rect(left + width - _topRight.DesiredSize.Width, top, _topRight.DesiredSize.Width, _topRight.DesiredSize.Height));
            _bottomLeft.Arrange(new Rect(left, top + height - _bottomLeft.DesiredSize.Height, _bottomLeft.DesiredSize.Width, _bottomLeft.DesiredSize.Height));
            _bottomRight.Arrange(new Rect(left + width - _bottomRight.DesiredSize.Width, top + height - _bottomRight.DesiredSize.Height, _bottomRight.DesiredSize.Width, _bottomRight.DesiredSize.Height));
            _middle.Arrange(new Rect(left, top, width, height));

            return finalSize;
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
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