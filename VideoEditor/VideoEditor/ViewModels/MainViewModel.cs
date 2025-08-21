// MainViewModel.cs

using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using VideoEditor.Models;
using VideoEditor.Common;
// ✅ 1. using 문을 새로운 라이브러리로 변경합니다.
using CommunityToolkit.Mvvm.Input;

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

        // ✅ 2. 비동기 작업을 위한 IAsyncRelayCommand 와 동기 작업을 위한 IRelayCommand를 사용합니다.
        public IAsyncRelayCommand PlayPauseTimelineCommand { get; }
        public IRelayCommand StopTimelineCommand { get; }

        private double _currentTimelinePosition;
        private bool _isStopRequested;

        public MainViewModel()
        {
            PlayerViewModel = new PlayerViewModel();
            VideoList = new VideoListViewModel();
            VideoEditor = new VideoEditorViewModel();

            VideoEditor.OnClipAdded += MainViewModel_OnClipAdded;

            // ✅ 3. 새로운 Command로 초기화합니다. 비동기 메서드는 AsyncRelayCommand를 사용합니다.
            PlayPauseTimelineCommand = new AsyncRelayCommand(ExecutePlayPauseTimelineAsync);
            StopTimelineCommand = new RelayCommand(ExecuteStopTimeline);

            PlayerViewModel.MediaPlayer.EndReached += OnClipFinished;
        }

        private void MainViewModel_OnClipAdded(object? sender, ClipAddedEventArgs e)
        {
            if (VideoEditor.TimelineClips.Count == 1)
            {
                Debug.WriteLine($"[EVENT] 첫 클립 추가됨: {e.VideoPath}. 미리보기를 위해 로드합니다.");
                PlayerViewModel.LoadMedia(e.VideoPath);
            }
        }

        // ✅ 4. Command가 호출할 메서드를 async Task로 변경하여 비동기 작업을 안전하게 처리합니다.
        private async Task ExecutePlayPauseTimelineAsync()
        {
            Debug.WriteLine($"[COMMAND] Play/Pause 버튼 클릭. 현재 재생 상태: {IsTimelinePlaying}");
            if (IsTimelinePlaying)
            {
                PlayerViewModel.Pause();
                IsTimelinePlaying = false;
                _isStopRequested = true;
            }
            else
            {
                if (PlayerViewModel.MediaPlayer.State == LibVLCSharp.Shared.VLCState.Paused)
                {
                    PlayerViewModel.Play();
                    IsTimelinePlaying = true;
                    _isStopRequested = false;
                }
                else
                {
                    _isStopRequested = false;
                    // Task.Run 없이 직접 await 합니다. 훨씬 안전하고 깔끔합니다.
                    await PlayTimelineFrom(_currentTimelinePosition);
                }
            }
        }

        private void ExecuteStopTimeline()
        {
            Debug.WriteLine("[COMMAND] Stop 버튼 클릭.");
            _isStopRequested = true;
            PlayerViewModel.Stop();
            VideoEditor.CurrentlyPlayingClip = null;
            _currentTimelinePosition = 0;
            IsTimelinePlaying = false;
        }

        // ✅ 5. SeekTimeline도 async void로 변경하여 비동기 Delay를 안전하게 처리합니다.
        public async void SeekTimeline(double timeSec)
        {
            Debug.WriteLine($"[SEEK] 타임라인 {timeSec:F2}초로 이동.");
            bool wasPlaying = IsTimelinePlaying;

            _isStopRequested = true;
            PlayerViewModel.Stop();
            VideoEditor.CurrentlyPlayingClip = null;
            IsTimelinePlaying = false;

            _currentTimelinePosition = timeSec;

            if (wasPlaying)
            {
                _isStopRequested = false;
                await Task.Delay(50); // Task.Run 없이 직접 await
                if (!_isStopRequested) // Delay 이후에도 여전히 재생 상태여야 한다면
                {
                    await PlayTimelineFrom(_currentTimelinePosition);
                }
            }
        }

        private async Task PlayTimelineFrom(double startTimeSec)
        {
            Debug.WriteLine($"▶️ PlayTimelineFrom 시작: {startTimeSec:F2}초 부터");
            UIDispatcher.Invoke(() => IsTimelinePlaying = true);
            _currentTimelinePosition = startTimeSec;

            if (_isStopRequested)
            {
                Debug.WriteLine("재생 중지 요청으로 PlayTimelineFrom 중단.");
                return;
            }

            var nextClip = VideoEditor.TimelineClips
                .Where(c => c.StartPosition + c.Duration > _currentTimelinePosition)
                .OrderBy(c => c.StartPosition)
                .FirstOrDefault();

            if (nextClip == null)
            {
                Debug.WriteLine("재생할 다음 클립이 없어 타임라인 재생을 종료합니다.");
                UIDispatcher.Invoke(ExecuteStopTimeline);
                return;
            }

            double gapDuration = nextClip.StartPosition - _currentTimelinePosition;

            if (gapDuration > 0.01)
            {
                Debug.WriteLine($"빈 공간 발견. 클립 시작까지 {gapDuration:F2}초 대기합니다.");
                VideoEditor.CurrentlyPlayingClip = null;
                await Task.Delay(TimeSpan.FromSeconds(gapDuration));

                if (_isStopRequested)
                {
                    Debug.WriteLine("딜레이 후 중지 요청이 감지되어 재생을 시작하지 않습니다.");
                    return;
                }
                _currentTimelinePosition = nextClip.StartPosition;
            }

            double timeWithinClip = _currentTimelinePosition - nextClip.StartPosition;
            if (timeWithinClip < 0) timeWithinClip = 0;

            Debug.WriteLine($"클립 '{nextClip.Name}' 재생 시작 (오프셋: {timeWithinClip:F2}초).");
            VideoEditor.CurrentlyPlayingClip = nextClip;

            UIDispatcher.Invoke(() => {
                PlayerViewModel.LoadMedia(nextClip.VideoPath);
                PlayerViewModel.MediaPlayer.Time = (long)(timeWithinClip * 1000);
                PlayerViewModel.Play();
            });
        }

        // ✅ 6. 이벤트 핸들러는 async void가 가장 적합한 패턴입니다.
        private async void OnClipFinished(object? sender, EventArgs e)
        {
            if (!IsTimelinePlaying || _isStopRequested || VideoEditor.CurrentlyPlayingClip == null)
            {
                Debug.WriteLine($"OnClipFinished: 재생이 중지되었거나 다음 클립을 재생할 수 없어 로직을 중단합니다.");
                return;
            }
            Debug.WriteLine($"클립 '{VideoEditor.CurrentlyPlayingClip.Name}' 재생 완료.");

            _currentTimelinePosition = VideoEditor.CurrentlyPlayingClip.StartPosition + VideoEditor.CurrentlyPlayingClip.Duration;

            // 직접 await 하여 다음 클립 재생
            await PlayTimelineFrom(_currentTimelinePosition);
        }
    }
}