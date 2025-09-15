using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoEditor.Models
{
    public class TextClip : TimelineClipBase
    {
        public string Text { get; set; } = "자막을 입력하세요";
        public override TimelineClipBase Clone()
        {
            var newClip = new TextClip
            {
                Name = this.Name + " (복사본)",
                Text = this.Text,
                StartPosition = this.StartPosition,
                Duration = this.Duration,
                Width = this.Width,
                TrackIndex = this.TrackIndex,
                IsSelected = false
            };
            return newClip;
        }
    }
}
