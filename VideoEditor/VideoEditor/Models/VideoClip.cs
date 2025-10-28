using System.Collections.ObjectModel;
using System.Windows.Media.Imaging;
using Newtonsoft.Json;

namespace VideoEditor.Models
{
    public class VideoClip : VisualClipBase
    {
        private double _sourceStartTime;
        private string _videoPath = string.Empty;
        private BitmapImage? _thumbnail;
        private bool _isMuted;

        public double SourceStartTime { get => _sourceStartTime; set => SetProperty(ref _sourceStartTime, value); }
        public string VideoPath { get => _videoPath; set => SetProperty(ref _videoPath, value); }

        [JsonIgnore]
        public BitmapImage? Thumbnail { get => _thumbnail; set => SetProperty(ref _thumbnail, value); }
        public string Category { get; set; } = "미분류";

        public int SourceWidth { get; set; }
        public int SourceHeight { get; set; }

        public bool IsMuted
        {
            get => _isMuted;
            set => SetProperty(ref _isMuted, value);
        }

        public ObservableCollection<TranscriptionSegment> Transcription { get; set; } = new();

        public VideoClip() { }

        public override (int Width, int Height) GetContentDimensions()
        {
            return (SourceWidth, SourceHeight);
        }

        public override TimelineClipBase Clone()
        {
            var newClip = new VideoClip
            {
                Name = this.Name + " (복사본)",

                // TimelineClipBase 속성
                StartPosition = this.StartPosition,
                Duration = this.Duration,
                SpeedRatio = this.SpeedRatio,
                Width = this.Width,
                TrackIndex = this.TrackIndex,
                IsSelected = false,
                Volume = this.Volume,
                GroupId = this.GroupId,

                // VisualClipBase 속성
                PositionX = this.PositionX,
                PositionY = this.PositionY,
                Scale = this.Scale,

                // VideoClip 고유 속성
                VideoPath = this.VideoPath,
                SourceStartTime = this.SourceStartTime,
                Thumbnail = this.Thumbnail,
                Category = this.Category,
                SourceWidth = this.SourceWidth,
                SourceHeight = this.SourceHeight,
                IsMuted = this.IsMuted,
            };

            // 전사 데이터도 복사
            foreach (var segment in this.Transcription)
            {
                newClip.Transcription.Add(segment);
            }

            return newClip;
        }
    }
}