using System.Collections.ObjectModel;

namespace VideoEditor.Models
{
    public class AudioClip : TimelineClipBase
    {
        public string AudioPath { get; set; } = string.Empty;
        public double SourceStartTime { get; set; }

        public ObservableCollection<TranscriptionSegment> Transcription { get; set; } = new();

        public override TimelineClipBase Clone()
        {
            var newClip = new AudioClip
            {
                Name = this.Name + " (복사본)",

                StartPosition = this.StartPosition,
                Duration = this.Duration,
                SpeedRatio = this.SpeedRatio,
                Width = this.Width,
                TrackIndex = this.TrackIndex,
                IsSelected = false,
                Volume = this.Volume,
                GroupId = this.GroupId,

                AudioPath = this.AudioPath,
                SourceStartTime = this.SourceStartTime,
            };

            foreach (var segment in this.Transcription)
            {
                newClip.Transcription.Add(segment);
            }

            return newClip;
        }
    }
}