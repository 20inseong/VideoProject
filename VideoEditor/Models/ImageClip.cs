using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace VideoEditor.Models
{
    public class ImageClip : TimelineClipBase
    {
        public string ImagePath { get; set; } = string.Empty;

        [JsonIgnore]
        public BitmapImage? Thumbnail { get; set; }
        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }

        // 회전 각도 (0-360도)
        private double _rotation = 0.0;
        public double Rotation
        {
            get => _rotation;
            set => SetProperty(ref _rotation, value);
        }

        // 투명도 (0-100%)
        private double _opacity = 100.0;
        public double Opacity
        {
            get => _opacity;
            set => SetProperty(ref _opacity, Math.Max(0, Math.Min(100, value)));
        }

        // 사용자 지정 크기 (원본 크기 기준, 픽셀 단위)
        private double _customWidth = 0;
        private bool _isUpdatingCustomSize = false; // 무한 루프 방지 플래그
        
        public double CustomWidth
        {
            get => _customWidth;
            set
            {
                // 무한 루프 방지: 값이 실제로 변경되었을 때만 처리
                if (Math.Abs(_customWidth - value) < 0.01 || _isUpdatingCustomSize) return;
                
                _isUpdatingCustomSize = true;
                try
                {
                    if (SetProperty(ref _customWidth, value) && value > 0 && SourceWidth > 0 && SourceHeight > 0)
                    {
                        // 비율에 맞게 높이 자동 조정
                        double aspectRatio = (double)SourceHeight / SourceWidth;
                        double newHeight = value * aspectRatio;
                        
                        if (Math.Abs(_customHeight - newHeight) >= 0.01)
                        {
                            SetProperty(ref _customHeight, newHeight, nameof(CustomHeight));
                        }
                        
                        // RenderWidth/Height 업데이트
                        UpdateRenderSizeFromCustomSize();
                    }
                }
                finally
                {
                    _isUpdatingCustomSize = false;
                }
            }
        }

        private double _customHeight = 0;
        public double CustomHeight
        {
            get => _customHeight;
            set
            {
                // 무한 루프 방지: 값이 실제로 변경되었을 때만 처리
                if (Math.Abs(_customHeight - value) < 0.01 || _isUpdatingCustomSize) return;
                
                _isUpdatingCustomSize = true;
                try
                {
                    if (SetProperty(ref _customHeight, value) && value > 0 && SourceWidth > 0 && SourceHeight > 0)
                    {
                        // 비율에 맞게 너비 자동 조정
                        double aspectRatio = (double)SourceWidth / SourceHeight;
                        double newWidth = value * aspectRatio;
                        
                        if (Math.Abs(_customWidth - newWidth) >= 0.01)
                        {
                            SetProperty(ref _customWidth, newWidth, nameof(CustomWidth));
                        }
                        
                        // RenderWidth/Height 업데이트
                        UpdateRenderSizeFromCustomSize();
                    }
                }
                finally
                {
                    _isUpdatingCustomSize = false;
                }
            }
        }

        // 초기 RenderWidth/Height 비율 저장 (미리보기 크기 기준)
        [JsonIgnore]
        public double InitialRenderWidth { get; set; }
        [JsonIgnore]
        public double InitialRenderHeight { get; set; }

        // CustomWidth/Height가 변경되면 RenderWidth/Height를 비례적으로 조정
        private void UpdateRenderSizeFromCustomSize()
        {
            if (SourceWidth > 0 && SourceHeight > 0 && _customWidth > 0 && _customHeight > 0 && InitialRenderWidth > 0 && InitialRenderHeight > 0)
            {
                // CustomWidth/Height가 SourceWidth/Height 대비 어느 정도 비율인지 계산
                double widthRatio = _customWidth / SourceWidth;
                double heightRatio = _customHeight / SourceHeight;
                
                // 초기 RenderWidth/Height에 비율을 적용
                RenderWidth = InitialRenderWidth * widthRatio;
                RenderHeight = InitialRenderHeight * heightRatio;
            }
        }

        // RenderWidth/Height가 변경되면 CustomWidth/Height를 업데이트
        public void UpdateCustomSizeFromRenderSize()
        {
            if (_isUpdatingCustomSize) return; // 이미 업데이트 중이면 무시
            
            if (SourceWidth > 0 && SourceHeight > 0 && InitialRenderWidth > 0 && InitialRenderHeight > 0 && RenderWidth > 0 && RenderHeight > 0)
            {
                _isUpdatingCustomSize = true;
                try
                {
                    // RenderWidth/Height가 InitialRenderWidth/Height 대비 어느 정도 비율인지 계산
                    double widthRatio = RenderWidth / InitialRenderWidth;
                    double heightRatio = RenderHeight / InitialRenderHeight;
                    
                    // SourceWidth/Height에 비율을 적용하여 CustomWidth/Height 계산
                    double newCustomWidth = SourceWidth * widthRatio;
                    double newCustomHeight = SourceHeight * heightRatio;
                    
                    if (Math.Abs(_customWidth - newCustomWidth) >= 0.01)
                    {
                        SetProperty(ref _customWidth, newCustomWidth, nameof(CustomWidth));
                    }
                    if (Math.Abs(_customHeight - newCustomHeight) >= 0.01)
                    {
                        SetProperty(ref _customHeight, newCustomHeight, nameof(CustomHeight));
                    }
                }
                finally
                {
                    _isUpdatingCustomSize = false;
                }
            }
        }

        public override TimelineClipBase Clone()
        {
            var newClip = new ImageClip
            {
                Name = this.Name + " (복사본)",
                ImagePath = this.ImagePath,
                Thumbnail = this.Thumbnail,
                SourceWidth = this.SourceWidth,
                SourceHeight = this.SourceHeight,
                Rotation = this.Rotation,
                Opacity = this.Opacity,
                CustomWidth = this.CustomWidth,
                CustomHeight = this.CustomHeight,
                InitialRenderWidth = this.InitialRenderWidth,
                InitialRenderHeight = this.InitialRenderHeight
            };
            
            // Copy all base properties including duration/speed
            newClip.CopyBaseProperties(this);
            
            return newClip;
        }
    }
}