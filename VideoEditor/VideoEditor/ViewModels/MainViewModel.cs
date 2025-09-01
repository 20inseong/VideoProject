using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using CommunityToolkit.Mvvm.Input;
using VideoEditor.Common;
using VideoEditor.Models;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using System.Globalization;

namespace VideoEditor.ViewModels
{
    public class MainViewModel : ViewModelBase
    {
        public PlayerViewModel PlayerViewModel { get; }
        public VideoListViewModel VideoList { get; }
        public VideoEditorViewModel VideoEditor { get; }
        public string StatusMessage { get; set; }

        private bool _isTimelinePlaying;
        public bool IsTimelinePlaying
        {
            get => _isTimelinePlaying;
            private set
            {
                if (SetProperty(ref _isTimelinePlaying, value))
                {
                    OnPropertyChanged(nameof(PlayPauseButtonContent));
                }
            }
        }

        public string PlayPauseButtonContent => IsTimelinePlaying ? "❚❚" : "▶";

        public IRelayCommand PlayPauseTimelineCommand { get; }
        public IRelayCommand StopTimelineCommand { get; }

        private double _currentTimelinePosition;
        public double CurrentTimelinePosition
        {
            get => _currentTimelinePosition;
            set
            {
                if (SetProperty(ref _currentTimelinePosition, value))
                {
                    OnPropertyChanged(nameof(CurrentTimelineTimeMs));
                }
            }
        }

        public long CurrentTimelineTimeMs
        {
            get => (long)(CurrentTimelinePosition * 1000);
            set
            {
                if (Math.Abs(value - (CurrentTimelinePosition * 1000)) < 100) return;

                SeekTimeline(value / 1000.0);
            }
        }

        private bool _isStopRequested;

        private long _totalTimelineDurationMs;
        public long TotalTimelineDurationMs
        {
            get => _totalTimelineDurationMs;
            private set => SetProperty(ref _totalTimelineDurationMs, value);
        }
        private CancellationTokenSource? _clipUpdateCts;

        private readonly DispatcherTimer _timelineTimer;

        public MainViewModel()
        {
            PlayerViewModel = new PlayerViewModel();
            VideoList = new VideoListViewModel();
            VideoEditor = new VideoEditorViewModel();

            VideoEditor.OnClipAdded += MainViewModel_OnClipAdded;

            VideoEditor.TimelineClips.CollectionChanged += (s, e) =>
            {
                UIDispatcher.Invoke(() =>
                {
                    // 새로 추가된 클립의 PropertyChanged 이벤트 구독
                    if (e.NewItems != null)
                    {
                        foreach (VideoClip newClip in e.NewItems)
                        {
                            newClip.PropertyChanged += Clip_PropertyChanged;
                        }
                    }
                    // 제거된 클립의 PropertyChanged 이벤트 구독 해지
                    if (e.OldItems != null)
                    {
                        foreach (VideoClip oldClip in e.OldItems)
                        {
                            oldClip.PropertyChanged -= Clip_PropertyChanged;
                        }
                    }
                    UpdateTotalTimelineDuration();
                });
            };

            PlayPauseTimelineCommand = new RelayCommand(ExecutePlayPauseTimeline);
            StopTimelineCommand = new RelayCommand(ExecuteStopTimeline);

            PlayerViewModel.MediaPlayer.EndReached += OnClipFinished;

            _timelineTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _timelineTimer.Tick += OnTimelineTimerTick;

            UpdateTotalTimelineDuration();
        }

        private void OnTimelineTimerTick(object? sender, EventArgs e)
        {
            if (!IsTimelinePlaying)
            {
                _timelineTimer.Stop();
                return;
            }

            // 1. 다음 프레임 위치 계산
            // (재생 속도 등을 고려하려면 더 복잡해지지만, 지금은 기본 속도로 가정)
            CurrentTimelinePosition += _timelineTimer.Interval.TotalSeconds;

            // 2. 현재 시간에 재생해야 할 클립을 찾습니다 (사용자님이 제안한 "하나의 함수")
            var clipToPlay = VideoEditor.TimelineClips
                .FirstOrDefault(c => c.StartPosition <= CurrentTimelinePosition && (c.StartPosition + c.Duration) > CurrentTimelinePosition);

            // 3. 상태 변화를 감지하고 Player에 명령을 내립니다.

            // CASE 1: 재생해야 할 클립이 있는데, 현재 아무것도 재생 중이 아니거나 다른 클립을 재생 중일 때
            if (clipToPlay != null && VideoEditor.CurrentlyPlayingClip != clipToPlay)
            {
                Debug.WriteLine($"[Timeline Tick] '{clipToPlay.Name}' 재생 시작.");
                VideoEditor.CurrentlyPlayingClip = clipToPlay;

                // 원본 영상의 어느 지점부터 재생할지 계산
                double timeWithinClip = CurrentTimelinePosition - clipToPlay.StartPosition;
                double seekTimeInSource = clipToPlay.SourceStartTime + timeWithinClip;

                PlayerViewModel.PlayMediaFrom(clipToPlay.VideoPath, (long)(seekTimeInSource * 1000));
            }
            // CASE 2: 빈 공간(Gap)에 도달했을 때 (재생할 클립이 없고, 현재 무언가 재생 중일 때)
            else if (clipToPlay == null && VideoEditor.CurrentlyPlayingClip != null)
            {
                Debug.WriteLine("[Timeline Tick] 빈 공간(Gap) 진입. 재생을 멈춥니다.");
                PlayerViewModel.Stop();
                VideoEditor.CurrentlyPlayingClip = null;
            }
            // CASE 3: 타임라인이 끝났을 때
            else if (CurrentTimelinePosition * 1000 >= TotalTimelineDurationMs)
            {
                Debug.WriteLine("[Timeline Tick] 타임라인 끝에 도달. 재생을 종료합니다.");
                ExecuteStopTimeline();
            }
        }

        private void Clip_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VideoClip.StartPosition) || e.PropertyName == nameof(VideoClip.Duration))
            {
                UpdateTotalTimelineDuration();
            }
        }

        private void MainViewModel_OnClipAdded(object? sender, ClipAddedEventArgs e)
        {
            if (VideoEditor.TimelineClips.Count == 1)
            {
                Debug.WriteLine($"[EVENT] 첫 클립 추가됨: {e.VideoPath}. 미리보기를 위해 로드합니다.");
                PlayerViewModel.LoadMedia(e.VideoPath);
            }
        }

        private void ExecutePlayPauseTimeline()
        {
            Debug.WriteLine($"[COMMAND] Play/Pause 버튼 클릭. 현재 재생 상태: {IsTimelinePlaying}");
            if (IsTimelinePlaying)
            {
                PlayerViewModel.Pause();
                _timelineTimer.Stop();
                IsTimelinePlaying = false;
            }
            else
            {
                IsTimelinePlaying = true;
                _timelineTimer.Start();
            }
        }

        private void ExecuteStopTimeline()
        {
            Debug.WriteLine("[COMMAND] Stop 버튼 클릭.");
            _timelineTimer.Stop();
            PlayerViewModel.Stop();
            VideoEditor.CurrentlyPlayingClip = null;
            CurrentTimelinePosition = 0;
            IsTimelinePlaying = false;
        }

        public void SeekTimeline(double timeSec)
        {
            Debug.WriteLine($"[SEEK] 타임라인 {timeSec:F2}초로 이동.");

            if (IsTimelinePlaying)
            {
                _timelineTimer.Stop();
                PlayerViewModel.Stop();
                IsTimelinePlaying = false; // 일단 정지
            }

            CurrentTimelinePosition = timeSec;
            VideoEditor.CurrentlyPlayingClip = null; // 현재 재생 클립 상태 초기화
        }

        private async void OnClipFinished(object? sender, EventArgs e)
        {
            Debug.WriteLine($"'{VideoEditor.CurrentlyPlayingClip?.Name}' 클립 재생 완료. 마스터 루프가 계속 진행합니다.");
        }

        private void UpdateTotalTimelineDuration()
        {
            long newTotalDurationMs;
             
            if (VideoEditor.TimelineClips.Any())
            {
                double maxEndTimeSec = VideoEditor.TimelineClips.Max(c => c.StartPosition + c.Duration);
                newTotalDurationMs = (long)(maxEndTimeSec * 1000);
            }
            else
            {
                newTotalDurationMs = 300 * 1000;
            }

            TotalTimelineDurationMs = newTotalDurationMs;
            PlayerViewModel.TotalDuration = newTotalDurationMs;

            Debug.WriteLine($"[Timeline Duration] 총 타임라인 길이 업데이트: {TotalTimelineDurationMs / 1000.0:F2}초");
        }
    }
}