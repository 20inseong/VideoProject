namespace VideoEditor.Models
{
    public class AudioClip : TimelineClipBase
    {
        public string AudioPath { get; set; } = string.Empty;
        public double SourceStartTime { get; set; }

        public override TimelineClipBase Clone()
        {
            var newClip = new AudioClip
            {
                Name = this.Name + " (복사본)",
                AudioPath = this.AudioPath,
                SourceStartTime = this.SourceStartTime,
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