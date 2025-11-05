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
        private double _originalDuration;
        private double _speedRatio = 1.0;
        private double _width;
        private int _trackIndex;
        private bool _isSelected;
        private int _volume = 100;

        private double _x;
        public double X 
        { 
            get => _x; 
            set 
            { 
                if (SetProperty(ref _x, value))
                {
                    // Mark as user-positioned when X is manually set (not during initial layout)
                    if (_initialLayoutComplete)
                        IsUserPositioned = true;
                }
            } 
        }

        private double _y;
        public double Y 
        { 
            get => _y; 
            set 
            { 
                if (SetProperty(ref _y, value))
                {
                    // Mark as user-positioned when Y is manually set (not during initial layout)
                    if (_initialLayoutComplete)
                        IsUserPositioned = true;
                }
            } 
        }

        private double _renderWidth;
        public double RenderWidth { get => _renderWidth; set => SetProperty(ref _renderWidth, value); }

        private double _renderHeight;
        public double RenderHeight { get => _renderHeight; set => SetProperty(ref _renderHeight, value); }

        public double Scale { get; set; } = 1.0;
        
        // Track if this clip has been manually positioned by the user
        public bool IsUserPositioned { get; set; } = false;
        
        // Track if initial layout has been completed
        private bool _initialLayoutComplete = false;

        private Guid? _groupId;
        public Guid? GroupId
        {
            get => _groupId;
            set => SetProperty(ref _groupId, value);
        }

        public Guid Id { get; } = Guid.NewGuid();

        public string Name { get => _name; set => SetProperty(ref _name, value); }
        public double StartPosition { get => _startPosition; set => SetProperty(ref _startPosition, value); }
        public double Duration
        {
            get => _duration;
            set
            {
                if (SetProperty(ref _duration, value))
                {
                        // Duration이 직접 설정된 경우, 1x 속도에서의 원래 지속 시간
                    SetProperty(ref _originalDuration, value, nameof(OriginalDuration));
                }
            }
        }
        public double OriginalDuration { get => _originalDuration; private set => SetProperty(ref _originalDuration, value); }
        public double SpeedRatio
        {
            get => _speedRatio;
            set
            {
                double clampedValue = Math.Max(0.1, Math.Min(value, 32.0));
                if (SetProperty(ref _speedRatio, clampedValue))
                {
                            // 속도 변경 시 타임라인 지속 시간을 재계산
                    SetProperty(ref _duration, OriginalDuration / _speedRatio, nameof(Duration));
                }
            }
        }
        public double Width { get => _width; set => SetProperty(ref _width, value); }
        public int TrackIndex { get => _trackIndex; set => SetProperty(ref _trackIndex, value); }
        public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
        public int Volume { get => _volume; set => SetProperty(ref _volume, value); }

        private bool _isMuted;
        public bool IsMuted
        {
            get => _isMuted;
            set => SetProperty(ref _isMuted, value);
        }

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

        private bool _isGeneratingWaveform;
        public bool IsGeneratingWaveform
        {
            get => _isGeneratingWaveform;
            set => SetProperty(ref _isGeneratingWaveform, value);
        }

        private System.Collections.Generic.List<System.Windows.Point> _waveformData = new System.Collections.Generic.List<System.Windows.Point>();
        public System.Collections.Generic.List<System.Windows.Point> WaveformData
        {
            get => _waveformData;
            set => SetProperty(ref _waveformData, value);
        }

        public IRelayCommand ShowTranscriptionCommand { get; }
        public IRelayCommand HideTranscriptionCommand { get; }

        public TimelineClipBase()
        {
            ShowTranscriptionCommand = new RelayCommand(() => ShowTranscription = true);
            HideTranscriptionCommand = new RelayCommand(() => ShowTranscription = false);
        }

        public abstract TimelineClipBase Clone();
        
        // Mark that initial layout is complete and future position changes are user-driven
        public void MarkInitialLayoutComplete()
        {
            _initialLayoutComplete = true;
        }

        protected void CopyBaseProperties(TimelineClipBase source)
        {
            // Copy basic timeline properties
            this.StartPosition = source.StartPosition;
            this.TrackIndex = source.TrackIndex;
            this.Width = source.Width;
            this.IsSelected = false;
            
            // Copy audio properties
            this.Volume = source.Volume;
            this.IsMuted = source.IsMuted;
            
            // Copy rendering properties
            this.X = source.X;
            this.Y = source.Y;
            this.RenderWidth = source.RenderWidth;
            this.RenderHeight = source.RenderHeight;
            this.Scale = source.Scale;
            
            // Copy waveform data
            this.WaveformData = new System.Collections.Generic.List<System.Windows.Point>(source.WaveformData);
            this.IsGeneratingWaveform = source.IsGeneratingWaveform;
            
            // Copy transcription properties
            this.IsTranscribed = source.IsTranscribed;
            this.ShowTranscription = source.ShowTranscription;
            
            // IMPORTANT: Copy duration/speed in correct order to avoid recalculation
            // First set the original duration directly
            SetProperty(ref _originalDuration, source.OriginalDuration, nameof(OriginalDuration));
            // Then set speed ratio which will recalculate duration correctly
            SetProperty(ref _speedRatio, source.SpeedRatio, nameof(SpeedRatio));
            // Finally set the calculated duration
            SetProperty(ref _duration, source.Duration, nameof(Duration));
        }

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