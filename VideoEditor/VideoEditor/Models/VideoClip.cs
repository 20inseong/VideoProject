using System;
using System.Windows.Media.Imaging;
using VideoEditor.Common;
using System.Collections.ObjectModel;

namespace VideoEditor.Models
{
    public class VideoClip : TimelineClipBase
    {
        private double _sourceStartTime;
        private string _videoPath = string.Empty;
        private BitmapImage? _thumbnail;

        public double SourceStartTime { get => _sourceStartTime; set => SetProperty(ref _sourceStartTime, value); }
        public string VideoPath { get => _videoPath; set => SetProperty(ref _videoPath, value); }
        public BitmapImage? Thumbnail { get => _thumbnail; set => SetProperty(ref _thumbnail, value); }
        public string Category { get; set; } = "미분류";

        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }

        public ObservableCollection<TranscriptionSegment> Transcription { get; set; } = new();

        public VideoClip() { }

        public VideoClip(VideoClip original)
        {
            this.Name = original.Name;
            this.StartPosition = original.StartPosition;
            this.Duration = original.Duration;
            this.Width = original.Width;
            this.TrackIndex = original.TrackIndex;
            this.IsSelected = false;

            this.SourceStartTime = original.SourceStartTime;
            this.VideoPath = original.VideoPath;
            this.Thumbnail = original.Thumbnail;
            this.Category = original.Category;

            this.SourceWidth = original.SourceWidth;
            this.SourceHeight = original.SourceHeight;
        }

        public override TimelineClipBase Clone()
        {
            return new VideoClip(this)
            {
                Name = this.Name + " (복사본)"
            };
        }
    }
}
