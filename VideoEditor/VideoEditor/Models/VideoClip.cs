using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace VideoEditor.Models
{
    class VideoClip
    {
        private string _name;
        private double _startPosition; // 타임라인 상의 시작 위치 (초)
        private double _startTime;    // 원본 영상에서의 시작 지점 (초)
        private double _duration;     // 클립 길이 (초)
        private double _width;        // UI 너비 (픽셀)
        private string _videoPath;    // 원본 비디오 경로
        private BitmapImage _thumbnail; // 대표 썸네일
        private int _trackIndex;      // 트랙 인덱스 (0-4)

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(nameof(Name)); }
        }

        public double StartPosition
        {
            get => _startPosition;
            set
            {
                _startPosition = value;
                // 너비도 함께 업데이트 => 현재 묘듈화를 진행하면서 메인에 있는 변수나 모델이 있어서 부득이하게 주석(삭제해도 무방)
                //if (App.Current.MainWindow is MainWindow mainWindow)
                //{
                //    Width = Duration * mainWindow.PixelsPerSecond;
                //}
                OnPropertyChanged(nameof(StartPosition));
            }
        }

        // 외부에서 전달받은 pixelsPerSecond 값으로 자신의 너비를 갱신
        public void UpdateWidth(double pixelsPerSecond)
        {
            this.Width = this.Duration * pixelsPerSecond;
        }

        public double StartTime
        {
            get => _startTime;
            set { _startTime = value; OnPropertyChanged(nameof(StartTime)); }
        }

        public double Duration
        {
            get => _duration;
            set { _duration = value; OnPropertyChanged(nameof(Duration)); }
        }

        public double Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(nameof(Width)); }
        }

        public string VideoPath
        {
            get => _videoPath;
            set { _videoPath = value; OnPropertyChanged(nameof(VideoPath)); }
        }

        public BitmapImage Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; OnPropertyChanged(nameof(Thumbnail)); }
        }

        public int TrackIndex
        {
            get => _trackIndex;
            set { _trackIndex = value; OnPropertyChanged(nameof(TrackIndex)); }
        }

        public string Category { get; set; } = "미분류";

        public Guid Id { get; } = Guid.NewGuid();

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

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
