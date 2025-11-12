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
                    if (_initialLayoutComplete)
                        IsUserPositioned = true;
                }
            } 
        }

        private double _renderWidth;
        public double RenderWidth { get => _renderWidth; set => SetProperty(ref _renderWidth, value); }

        private double _renderHeight;
        public double RenderHeight { get => _renderHeight; set => SetProperty(ref _renderHeight, value); }

        public double ReferenceX { get; set; }
        public double ReferenceY { get; set; }
        public double ReferenceRenderWidth { get; set; }
        public double ReferenceRenderHeight { get; set; }

        public double Scale { get; set; } = 1.0;
        
        public bool IsUserPositioned { get; set; } = false;
        
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

        private bool _isAnalyzingEmotion;
        public bool IsAnalyzingEmotion
        {
            get => _isAnalyzingEmotion;
            set => SetProperty(ref _isAnalyzingEmotion, value);
        }

        private bool _isEmotionAnalyzed;
        public bool IsEmotionAnalyzed
        {
            get => _isEmotionAnalyzed;
            set => SetProperty(ref _isEmotionAnalyzed, value);
        }

        private bool _showEmotionAnalysis;
        public bool ShowEmotionAnalysis
        {
            get => _showEmotionAnalysis;
            set => SetProperty(ref _showEmotionAnalysis, value);
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

        public IRelayCommand ShowEmotionAnalysisCommand { get; }
        public IRelayCommand HideEmotionAnalysisCommand { get; }

        public TimelineClipBase()
        {
            ShowTranscriptionCommand = new RelayCommand(() => ShowTranscription = true);
            HideTranscriptionCommand = new RelayCommand(() => ShowTranscription = false);

            ShowEmotionAnalysisCommand = new RelayCommand(() => ShowEmotionAnalysis = true);
            HideEmotionAnalysisCommand = new RelayCommand(() => ShowEmotionAnalysis = false);
        }

        public abstract TimelineClipBase Clone();
        
        public void MarkInitialLayoutComplete()
        {
            _initialLayoutComplete = true;
        }

        protected void CopyBaseProperties(TimelineClipBase source)
        {
            this.StartPosition = source.StartPosition;
            this.TrackIndex = source.TrackIndex;
            this.Width = source.Width;
            this.IsSelected = false;
            
            this.Volume = source.Volume;
            this.IsMuted = source.IsMuted;
            
            this.X = source.X;
            this.Y = source.Y;
            this.RenderWidth = source.RenderWidth;
            this.RenderHeight = source.RenderHeight;
            this.Scale = source.Scale;
            
            this.WaveformData = new System.Collections.Generic.List<System.Windows.Point>(source.WaveformData);
            this.IsGeneratingWaveform = source.IsGeneratingWaveform;
            
            this.IsTranscribed = source.IsTranscribed;
            this.ShowTranscription = source.ShowTranscription;
            
            SetProperty(ref _originalDuration, source.OriginalDuration, nameof(OriginalDuration));
            SetProperty(ref _speedRatio, source.SpeedRatio, nameof(SpeedRatio));
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