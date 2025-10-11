using VideoEditor.Common;
using System;

namespace VideoEditor.Models
{
    public abstract class VisualClipBase : TimelineClipBase
    {
        private double _positionX;
        private double _positionY;
        private double _scale = 1.0;

        private double _playerWidth;
        private double _playerHeight;

        public abstract (int Width, int Height) GetContentDimensions();

        public void UpdateRenderContext(double playerWidth, double playerHeight)
        {
            _playerWidth = playerWidth;
            _playerHeight = playerHeight;

            Scale = _scale;
            PositionX = _positionX;
            PositionY = _positionY;
        }

        public double PositionX
        {
            get => _positionX;
            set
            {
                if (_playerWidth <= 0)
                {
                    SetProperty(ref _positionX, value);
                    return;
                }
                double finalRenderedWidth = GetBaseRenderedWidth() * _scale;
                double maxOffset = Math.Abs(_playerWidth - finalRenderedWidth) / 2.0;
                double clampedValue = Math.Clamp(value, -maxOffset, maxOffset);
                SetProperty(ref _positionX, clampedValue);
            }
        }

        public double PositionY
        {
            get => _positionY;
            set
            {
                if (_playerHeight <= 0)
                {
                    SetProperty(ref _positionY, value);
                    return;
                }
                double finalRenderedHeight = GetBaseRenderedHeight() * _scale;
                double maxOffset = Math.Abs(_playerHeight - finalRenderedHeight) / 2.0;
                double clampedValue = Math.Clamp(value, -maxOffset, maxOffset);
                SetProperty(ref _positionY, clampedValue);
            }
        }

        public double Scale
        {
            get => _scale;
            set
            {
                double maxScale = 10.0;
                if (_playerWidth > 0 && _playerHeight > 0)
                {
                    double baseRenderedWidth = GetBaseRenderedWidth();
                    double baseRenderedHeight = GetBaseRenderedHeight();

                    if (baseRenderedWidth > 0 && baseRenderedHeight > 0)
                    {
                        double scaleToFillWidth = _playerWidth / baseRenderedWidth;
                        double scaleToFillHeight = _playerHeight / baseRenderedHeight;

                        maxScale = Math.Max(scaleToFillWidth, scaleToFillHeight);
                    }
                }

                double clampedValue = Math.Clamp(value, 0.1, maxScale);

                if (SetProperty(ref _scale, clampedValue))
                {
                    PositionX = _positionX;
                    PositionY = _positionY;
                }
            }
        }

        private double GetBaseRenderedWidth()
        {
            if (_playerWidth <= 0 || _playerHeight <= 0) return 0;
            if (this is TextClip) return _playerWidth;
            var (contentWidth, contentHeight) = GetContentDimensions();
            if (contentWidth <= 0 || contentHeight <= 0) return 0;
            double playerAspect = _playerWidth / _playerHeight;
            double contentAspect = (double)contentWidth / contentHeight;
            return contentAspect > playerAspect ? _playerWidth : _playerHeight * contentAspect;
        }

        private double GetBaseRenderedHeight()
        {
            if (_playerWidth <= 0 || _playerHeight <= 0) return 0;
            if (this is TextClip) return _playerHeight;
            var (contentWidth, contentHeight) = GetContentDimensions();
            if (contentWidth <= 0 || contentHeight <= 0) return 0;
            double playerAspect = _playerWidth / _playerHeight;
            double contentAspect = (double)contentWidth / contentHeight;
            return contentAspect > playerAspect ? _playerWidth / contentAspect : _playerHeight;
        }

        public override abstract TimelineClipBase Clone();
    }
}