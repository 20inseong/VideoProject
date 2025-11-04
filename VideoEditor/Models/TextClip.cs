using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VideoEditor.Models
{
    public class TextClip : TimelineClipBase
    {
        private string _text = "자막을 입력하세요";
        public string Text
        {
            get => _text;
            set => SetProperty(ref _text, value);
        }

        private double _fontSize = 14.0;
        public double FontSize
        {
            get => _fontSize;
            set => SetProperty(ref _fontSize, value);
        }

        public override TimelineClipBase Clone()
        {
            var newClip = new TextClip
            {
                Name = this.Name + " (복사본)",
                Text = this.Text,
                FontSize = this.FontSize
            };
            
            // Copy all base properties including duration/speed
            newClip.CopyBaseProperties(this);
            
            return newClip;
        }
    }
}
