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
                // [핵심 수정] 최대 배율을 동적으로 계산합니다.
                double maxScale = 10.0; // 기본 최대 배율
                if (_playerWidth > 0 && _playerHeight > 0)
                {
                    double baseRenderedWidth = GetBaseRenderedWidth();
                    double baseRenderedHeight = GetBaseRenderedHeight();

                    // 0으로 나누는 오류를 방지합니다.
                    if (baseRenderedWidth > 0 && baseRenderedHeight > 0)
                    {
                        // 가로를 꽉 채우는 데 필요한 배율
                        double scaleToFillWidth = _playerWidth / baseRenderedWidth;
                        // 세로를 꽉 채우는 데 필요한 배율
                        double scaleToFillHeight = _playerHeight / baseRenderedHeight;

                        // 둘 중 더 큰 값이, 가로 또는 세로 중 하나가 먼저 뷰포트에 닿게 되는 배율입니다.
                        maxScale = Math.Max(scaleToFillWidth, scaleToFillHeight);
                    }
                }

                // 최소 배율(0.1)과 방금 계산한 동적 최대 배율 사이로 값을 제한(Clamp)합니다.
                double clampedValue = Math.Clamp(value, 0.1, maxScale);

                if (SetProperty(ref _scale, clampedValue))
                {
                    PositionX = _positionX;
                    PositionY = _positionY;
                }
            }
        }

        // 이 헬퍼 메서드들은 수정할 필요 없이 그대로 완벽하게 동작합니다.
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