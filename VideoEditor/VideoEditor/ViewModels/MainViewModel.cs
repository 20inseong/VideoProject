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

            VideoEditor.TimelineClips.CollectionChanged += TimelineClips_CollectionChanged;

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
            if (PlayerViewModel.MediaPlayer.IsPlaying && VideoEditor.CurrentlyPlayingClip != null)
            {
                // 현재 재생 위치를 업데이트 (데드락 없이 안전하게)
                CurrentTimelinePosition = VideoEditor.CurrentlyPlayingClip.StartPosition + (PlayerViewModel.MediaPlayer.Time / 1000.0);
            }
        }

        private void TimelineClips_CollectionChanged(object? sender, System.Collections.Specialized.NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (VideoClip clip in e.NewItems)
                {
                    clip.PropertyChanged += Clip_PropertyChanged;
                }
            }

            if (e.OldItems != null)
            {
                foreach (VideoClip clip in e.OldItems)
                {
                    clip.PropertyChanged -= Clip_PropertyChanged;
                }
            }

            if (VideoEditor.TimelineClips.Any())
            {
                PlayerViewModel.VideoViewBackground = Brushes.Black;
            }
            else
            {
                PlayerViewModel.VideoViewBackground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#525252"));
            }

            UpdateTotalTimelineDuration();
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
                IsTimelinePlaying = false;
                _isStopRequested = true;
                _timelineTimer.Stop();
            }
            else
            {
                _isStopRequested = false;
                if (PlayerViewModel.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Paused)
                {
                    PlayerViewModel.Play();
                    IsTimelinePlaying = true;
                    _timelineTimer.Start();
                }
                else
                {
                    _ = PlayTimelineFrom(_currentTimelinePosition);
                }
            }
        }

        private void ExecuteStopTimeline()
        {
            Debug.WriteLine("[COMMAND] Stop 버튼 클릭.");
            _isStopRequested = true;
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
                _isStopRequested = true;
                _timelineTimer.Stop();
                PlayerViewModel.Stop();
                IsTimelinePlaying = false;
            }

            VideoEditor.CurrentlyPlayingClip = null;
            CurrentTimelinePosition = timeSec;
        }

        private async Task PlayTimelineFrom(double startTimeSec)
        {
            Debug.WriteLine($"▶️ PlayTimelineFrom 시작: {startTimeSec:F2}초 부터");
            IsTimelinePlaying = true;
            CurrentTimelinePosition = startTimeSec;

            while (IsTimelinePlaying && !_isStopRequested)
            {
                var nextClip = VideoEditor.TimelineClips
                    .Where(c => c.StartPosition + c.Duration > CurrentTimelinePosition)
                    .OrderBy(c => c.StartPosition)
                    .FirstOrDefault();

                if (nextClip == null)
                {
                    Debug.WriteLine("재생할 다음 클립이 없어 타임라인 재생을 종료합니다.");
                    UIDispatcher.Invoke(ExecuteStopTimeline);
                    return;
                }
                double gapStartTime = CurrentTimelinePosition;
                double gapEndTime = nextClip.StartPosition;
                if (gapEndTime > gapStartTime)
                {
                    Debug.WriteLine($"빈 공간 발견. {gapStartTime:F2}초 부터 {gapEndTime:F2}초 까지 진행합니다.");
                    VideoEditor.CurrentlyPlayingClip = null;
                    UIDispatcher.Invoke(() => PlayerViewModel.Stop());

                    while (CurrentTimelinePosition < gapEndTime && !_isStopRequested)
                    {
                        await Task.Delay(100);
                        CurrentTimelinePosition = Math.Min(gapEndTime, CurrentTimelinePosition + 0.1);
                    }
                    if (_isStopRequested) break;
                }

                double timeWithinClip = CurrentTimelinePosition - nextClip.StartPosition;
                if (timeWithinClip < 0) timeWithinClip = 0;

                //CurrentTimelinePosition = nextClip.StartPosition; // 정확한 위치 보정
                //double timeWithinClip = CurrentTimelinePosition - nextClip.StartPosition;
                //if (timeWithinClip < 0) timeWithinClip = 0; // 혹시 모를 오차 방지

                Debug.WriteLine($"클립 '{nextClip.Name}' 재생 시작 (오프셋: {timeWithinClip:F2}초).");
                VideoEditor.CurrentlyPlayingClip = nextClip;

                var clipPlaybackTcs = new TaskCompletionSource<bool>();
                EventHandler<EventArgs>? onEndReachedHandler = null;
                onEndReachedHandler = (s, e) => {
                    PlayerViewModel.MediaPlayer.EndReached -= onEndReachedHandler;
                    clipPlaybackTcs.TrySetResult(true);
                };
                PlayerViewModel.MediaPlayer.EndReached += onEndReachedHandler;


                UIDispatcher.Invoke(() => {
                    // 기존 미디어가 있다면 해제합니다.
                    PlayerViewModel.MediaPlayer.Media?.Dispose();

                    // :start-time 옵션을 사용하여 새 미디어를 생성합니다.
                    var media = new Media(
                        PlayerViewModel._libVLC, // internal로 바꾼 _libVLC 사용
                        new Uri(nextClip.VideoPath),
                        $":start-time={timeWithinClip.ToString(CultureInfo.InvariantCulture)}"
                    );

                    // 새로 만든 미디어를 플레이어에 할당하고 재생합니다.
                    PlayerViewModel.MediaPlayer.Media = media;
                    PlayerViewModel.Play();
                });

                _timelineTimer.Start();
                await clipPlaybackTcs.Task;
                _timelineTimer.Stop();

                if (_isStopRequested) break;

                CurrentTimelinePosition = nextClip.StartPosition + nextClip.Duration;

            }

            if (!_isStopRequested)
            {
                UIDispatcher.Invoke(ExecuteStopTimeline);
            }
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