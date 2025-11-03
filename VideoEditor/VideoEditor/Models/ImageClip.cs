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

        public override TimelineClipBase Clone()
        {
            var newClip = new ImageClip
            {
                Name = this.Name + " (복사본)",
                ImagePath = this.ImagePath,
                Thumbnail = this.Thumbnail,
                StartPosition = this.StartPosition,
                Duration = this.Duration,
                Width = this.Width,
                TrackIndex = this.TrackIndex,
                IsSelected = false,
                SourceWidth = this.SourceWidth,
                SourceHeight = this.SourceHeight
            };
            return newClip;
        }
    }
}