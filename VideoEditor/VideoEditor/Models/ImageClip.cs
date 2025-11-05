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

        private double _opacity = 1.0;
        public double Opacity { get => _opacity; set => SetProperty(ref _opacity, value); }

        private double _rotation = 0.0;
        public double Rotation { get => _rotation; set => SetProperty(ref _rotation, value); }

        public override TimelineClipBase Clone()
        {
            var newClip = new ImageClip
            {
                Name = this.Name + " (복사본)",
                ImagePath = this.ImagePath,
                Thumbnail = this.Thumbnail,
                SourceWidth = this.SourceWidth,
                SourceHeight = this.SourceHeight,
                Opacity = this.Opacity,
                Rotation = this.Rotation
            };
            
            // Copy all base properties including duration/speed
            newClip.CopyBaseProperties(this);
            
            return newClip;
        }
    }
}