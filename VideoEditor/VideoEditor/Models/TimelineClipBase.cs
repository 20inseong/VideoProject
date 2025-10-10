using VideoEditor.Common;
using System.Runtime.CompilerServices;
using CommunityToolkit.Mvvm.Input;

namespace VideoEditor.Models
{
    public abstract class TimelineClipBase : ViewModelBase
    {
        private string _name = string.Empty;
        private double _startPosition;
        private double _duration;
        private double _width;
        private int _trackIndex;
        private bool _isSelected;

        public Guid Id { get; } = Guid.NewGuid();

        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public double StartPosition { get => _startPosition; set => SetProperty(ref _startPosition, value); }
        public double Duration { get => _duration; set => SetProperty(ref _duration, value); }
        public double Width { get => _width; set => SetProperty(ref _width, value); }
        public int TrackIndex { get => _trackIndex; set => SetProperty(ref _trackIndex, value); }
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }

        private bool _isTranscribing;
        public bool IsTranscribing
        {
            get => _isTranscribing;
            set => SetProperty(ref _isTranscribing, value);
        }

        private bool _isTranscribed;
        public bool IsTranscribed
        {
            get => _isTranscribed;
            set => SetProperty(ref _isTranscribed, value);
        }

        private bool _showTranscription;
        public bool ShowTranscription
        {
            get => _showTranscription;
            set => SetProperty(ref _showTranscription, value);
        }

        public IRelayCommand ShowTranscriptionCommand { get; }
        public IRelayCommand HideTranscriptionCommand { get; }

        public TimelineClipBase()
        {
            ShowTranscriptionCommand = new RelayCommand(() => ShowTranscription = true);
            HideTranscriptionCommand = new RelayCommand(() => ShowTranscription = false);
        }

        public abstract TimelineClipBase Clone();

        public void UpdateWidth(double pixelsPerSecond)
        {
            this.Width = this.Duration * pixelsPerSecond;
        }

        public new void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            base.OnPropertyChanged(propertyName);
        }
    }
}