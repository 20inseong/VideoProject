using System.Windows.Media.Imaging;

namespace VideoEditor.Models
{
    public class ImageClip : VisualClipBase
    {
        public string ImagePath { get; set; } = string.Empty;
        public BitmapImage? Thumbnail { get; set; }
        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }
        public override (int Width, int Height) GetContentDimensions()
        {
            return (SourceWidth, SourceHeight);
        }
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
                SourceHeight = this.SourceHeight,
                PositionX = this.PositionX,
                PositionY = this.PositionY,
                Scale = this.Scale,
            };
            return newClip;
        }
    }
}