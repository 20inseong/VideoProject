using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using VideoEditor.Common;
using VideoEditor.ViewModels;

namespace VideoEditor.Models
{
    public class VideoClip : ViewModelBase
    {
        private string _name;
        private double _startPosition;
        private double _startTime;
        private double _duration;
        private double _width;
        private string _videoPath;
        private BitmapImage _thumbnail;
        private int _trackIndex;
        private bool _isSelected;
        private double _playbackRate = 1.0;
        private double _scaleX = 1.0;
        private double _scaleY = 1.0;
        private double _rotation = 0.0;

        public string Name
        {
            get => _name;
            set => SetProperty(ref _name, value);
        }

        public double StartPosition
        {
            get => _startPosition;
            set => SetProperty(ref _startPosition, value);
        }

        public double StartTime
        {
            get => _startTime;
            set => SetProperty(ref _startTime, value);
        }

        public double Duration
        {
            get => _duration;
            set => SetProperty(ref _duration, value);
        }

        public double Width
        {
            get => _width;
            set => SetProperty(ref _width, value);
        }

        public string VideoPath
        {
            get => _videoPath;
            set => SetProperty(ref _videoPath, value);
        }

        public BitmapImage Thumbnail
        {
            get => _thumbnail;
            set => SetProperty(ref _thumbnail, value);
        }

        public int TrackIndex
        {
            get => _trackIndex;
            set => SetProperty(ref _trackIndex, value);
        }

        public string Category { get; set; } = "미분류";

        public Guid Id { get; } = Guid.NewGuid();
        public VideoLayerViewModel? AssociatedLayer { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set => SetProperty(ref _isSelected, value);
        }

        public double PlaybackRate
        {
            get => _playbackRate;
            set => SetProperty(ref _playbackRate, value);
        }

        public double ScaleX
        {
            get => _scaleX;
            set => SetProperty(ref _scaleX, value);
        }

        public double ScaleY
        {
            get => _scaleY;
            set => SetProperty(ref _scaleY, value);
        }

        public double Rotation
        {
            get => _rotation;
            set => SetProperty(ref _rotation, value);
        }

        public void UpdateWidth(double pixelsPerSecond)
        {
            this.Width = this.Duration * pixelsPerSecond;
        }

        // 클립 복사 메소드
        public VideoClip Clone()
        {
            return new VideoClip
            {
                Name = this.Name + " (복사본)",
                StartPosition = this.StartPosition,
                StartTime = this.StartTime,
                Duration = this.Duration,
                Width = this.Width,
                VideoPath = this.VideoPath,
                Thumbnail = this.Thumbnail,
                Category = this.Category,
                TrackIndex = this.TrackIndex
            };
        }
    }
}
