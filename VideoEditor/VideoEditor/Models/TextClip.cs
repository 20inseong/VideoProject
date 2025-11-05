using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;

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

        private string _fontFamily = "맑은 고딕";
        public string FontFamily { get => _fontFamily; set => SetProperty(ref _fontFamily, value); }

        private Color _foregroundColor = Colors.White;
        public Color ForegroundColor { get => _foregroundColor; set => SetProperty(ref _foregroundColor, value); }

        private double _fontSize = 14.0;
        public double FontSize
        {
            get => _fontSize;
            set => SetProperty(ref _fontSize, value);
        }

        private double _opacity = 1.0;
        public double Opacity { get => _opacity; set => SetProperty(ref _opacity, value); }

        private double _rotation = 0.0;
        public double Rotation { get => _rotation; set => SetProperty(ref _rotation, value); }

        public override TimelineClipBase Clone()
        {
            var newClip = new TextClip
            {
                Name = this.Name + " (복사본)",
                Text = this.Text,
                FontSize = this.FontSize,
                FontFamily = this.FontFamily,
                ForegroundColor = this.ForegroundColor,
                Opacity = this.Opacity,
                Rotation = this.Rotation
            };
            
            newClip.CopyBaseProperties(this);
            
            return newClip;
        }
    }
}
