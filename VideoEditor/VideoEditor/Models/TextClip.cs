using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoEditor.Models
{
    public class TextClip : VisualClipBase
    {
        private string _text = "자막을 입력하세요";
        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }
        public override (int Width, int Height) GetContentDimensions()
        {
            return (0, 0);
        }
        public override TimelineClipBase Clone()
        {
            var newClip = new TextClip
            {
                Name = this.Name + " (복사본)",
                Text = this.Text, // 복사할 때도 새로운 속성을 사용
                StartPosition = this.StartPosition,
                Duration = this.Duration,
                Width = this.Width,
                TrackIndex = this.TrackIndex,
                IsSelected = false,
                PositionX = this.PositionX,
                PositionY = this.PositionY,
                Scale = this.Scale,
            };
            return newClip;
        }
    }
}
