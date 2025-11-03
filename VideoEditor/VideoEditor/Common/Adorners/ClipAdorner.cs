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

        public ClipAdorner(UIElement adornedElement, TimelineClipBase clip) : base(adornedElement)
        {
            _clip = clip;
            _visuals = new VisualCollection(this);
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

            _topLeft.DragDelta += TopLeft_DragDelta;
            _topRight.DragDelta += TopRight_DragDelta;
            _bottomLeft.DragDelta += BottomLeft_DragDelta;
            _bottomRight.DragDelta += BottomRight_DragDelta;
            
            _middle.DragStarted += Middle_DragStarted;
            _middle.DragDelta += Middle_DragDelta;
            _middle.DragCompleted += Middle_DragCompleted;

            _visuals.Add(_topLeft);
            _visuals.Add(_topRight);
            _visuals.Add(_bottomLeft);
            _visuals.Add(_bottomRight);
            _visuals.Add(_middle);
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
                VerticalAlignment = VerticalAlignment.Stretch
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
            double newWidth = _clip.RenderWidth - e.HorizontalChange;
            double newHeight = _clip.RenderHeight - e.VerticalChange;

            if (newWidth > 0 && newHeight > 0)
            {
                _clip.X += e.HorizontalChange;
                _clip.Y += e.VerticalChange;
                _clip.RenderWidth = newWidth;
                _clip.RenderHeight = newHeight;
            }
        }

        private void TopRight_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = _clip.RenderWidth + e.HorizontalChange;
            double newHeight = _clip.RenderHeight - e.VerticalChange;

            if (newWidth > 0 && newHeight > 0)
            {
                _clip.Y += e.VerticalChange;
                _clip.RenderWidth = newWidth;
                _clip.RenderHeight = newHeight;
            }
        }

        private void BottomLeft_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = _clip.RenderWidth - e.HorizontalChange;
            double newHeight = _clip.RenderHeight + e.VerticalChange;

            if (newWidth > 0 && newHeight > 0)
            {
                _clip.X += e.HorizontalChange;
                _clip.RenderWidth = newWidth;
                _clip.RenderHeight = newHeight;
            }
        }

        private void BottomRight_DragDelta(object sender, DragDeltaEventArgs e)
        {
            double newWidth = _clip.RenderWidth + e.HorizontalChange;
            double newHeight = _clip.RenderHeight + e.VerticalChange;

            if (newWidth > 0 && newHeight > 0)
            {
                _clip.RenderWidth = newWidth;
                _clip.RenderHeight = newHeight;
            }
        }

        private void Middle_DragStarted(object sender, DragStartedEventArgs e)
        {
            // Store initial position when drag starts
            _initialX = _clip.X;
            _initialY = _clip.Y;
        }

        private void Middle_DragDelta(object sender, DragDeltaEventArgs e)
        {
            // Update position based on drag delta
            _clip.X += e.HorizontalChange;
            _clip.Y += e.VerticalChange;
        }

        private void Middle_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            // Drag completed - position should be final
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
            _topLeft.Arrange(new Rect(left - _topLeft.DesiredSize.Width / 2, top - _topLeft.DesiredSize.Height / 2, _topLeft.DesiredSize.Width, _topLeft.DesiredSize.Height));
            // Top-Right
            _topRight.Arrange(new Rect(left + width - _topRight.DesiredSize.Width / 2, top - _topRight.DesiredSize.Height / 2, _topRight.DesiredSize.Width, _topRight.DesiredSize.Height));
            // Bottom-Left
            _bottomLeft.Arrange(new Rect(left - _bottomLeft.DesiredSize.Width / 2, top + height - _bottomLeft.DesiredSize.Height / 2, _bottomLeft.DesiredSize.Width, _bottomLeft.DesiredSize.Height));
            // Bottom-Right
            _bottomRight.Arrange(new Rect(left + width - _bottomRight.DesiredSize.Width / 2, top + height - _bottomRight.DesiredSize.Height / 2, _bottomRight.DesiredSize.Width, _bottomRight.DesiredSize.Height));
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