using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using Emgu.CV;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using VideoEditor.Common;
using VideoEditor.Models;
using VideoEditor.Services;

namespace VideoEditor.ViewModels
{

    public class ExportStartedEventArgs : EventArgs
    {
        public ExportProgressViewModel ProgressViewModel { get; }
        public ExportStartedEventArgs(ExportProgressViewModel viewModel)
        {
            ProgressViewModel = viewModel;
        }
    }

    public class MainViewModel : ViewModelBase
    {
        private readonly ProjectService _projectService;
        private readonly FFmpegExportService _ffmpegExportService;
        public PlayerViewModel PlayerViewModel { get; }
        public VideoListViewModel VideoList { get; }
        public VideoEditorViewModel VideoEditor { get; }
        public EditorHostViewModel EditorHost { get; }
        public string StatusMessage { get; set; } = "준비 완료";
        public IAsyncRelayCommand ExportVideoCommand { get; }
        public IAsyncRelayCommand TranscribeVideoCommand { get; }
        public IAsyncRelayCommand SaveProjectCommand { get; }
        public IAsyncRelayCommand LoadProjectCommand { get; }

        private bool _isTranscribing;
        public bool IsTranscribing
        {
            get => _isTranscribing;
            set => SetProperty(ref _isTranscribing, value);
        }

        private int _transcriptionProgress;
        public int TranscriptionProgress
        {
            get => _transcriptionProgress;
            set => SetProperty(ref _transcriptionProgress, value);
        }

        private readonly SpeechToTextService _speechToTextService;

        public event EventHandler<ExportStartedEventArgs>? ExportStarted;
        public event EventHandler? VideoClipZOrderChanged;
        //public event EventHandler? ExportFinished;
        private Window? _mainWindow;
        private TranscriptionProgressWindow? _transcriptionProgressWindow;
        private CancellationTokenSource? _exportCts;

        public ObservableCollection<TimelineClipBase> ActiveVideoClips { get; } = new();
        public ObservableCollection<TimelineClipBase> ActiveWpfOverlays { get; } = new();




        private double _playerHostWidth = 1;
        private double _previousPlayerHostWidth = 1;
        public double PlayerHostWidth
        {
            get => _playerHostWidth;
            set
            {
                if (SetProperty(ref _playerHostWidth, value))
                {
                    UpdateClipsForPlayerSizeChange();
                    _previousPlayerHostWidth = value;
                }
            }
        }

        private double _playerHostHeight = 1;
        private double _previousPlayerHostHeight = 1;
        public double PlayerHostHeight
        {
            get => _playerHostHeight;
            set
            {
                if (SetProperty(ref _playerHostHeight, value))
                {
                    UpdateClipsForPlayerSizeChange();
                    _previousPlayerHostHeight = value;
                }
            }
        }

        private bool _isPerformanceWarningVisible;
        public bool IsPerformanceWarningVisible
        {
            get => _isPerformanceWarningVisible;
            set => SetProperty(ref _isPerformanceWarningVisible, value);
        }

        private string _performanceWarningMessage = string.Empty;
        public string PerformanceWarningMessage
        {
            get => _performanceWarningMessage;
            set => SetProperty(ref _performanceWarningMessage, value);
        }

        private int _performanceWarningCounter;
        private DispatcherTimer _performanceWarningTimer;

        public MainViewModel(Window mainWindow) : this()
        {
            _mainWindow = mainWindow;
        }

        private bool _isExporting;
        public bool IsExporting
        {
            get => _isExporting;
            set => SetProperty(ref _isExporting, value);
        }

        private double _exportProgress;
        public double ExportProgress
        {
            get => _exportProgress;
            set => SetProperty(ref _exportProgress, value);
        }

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
                bool firstScrub = !IsScrubbing;
                if (firstScrub)
                {
                    _wasPlayingBeforeInteraction = IsTimelinePlaying;
                    if (IsTimelinePlaying)
                    {
                        _timelineTimer.Stop();
                        PlayerViewModel.PauseAllPlayers();
                        IsTimelinePlaying = false;
                    }
                }

                IsScrubbing = true;
                _scrubSeekTimer.Stop();
                _scrubSeekTimer.Start();
                _scrubbingTimer.Stop();
                _scrubbingTimer.Start();

                if (SetProperty(ref _currentTimelinePosition, value / 1000.0, nameof(CurrentTimelinePosition)))
                {
                    OnPropertyChanged(nameof(CurrentTimelineTimeMs));
                }
            }
        }

        private void ScrubbingTimer_Tick(object? sender, EventArgs e)
        {
            _scrubbingTimer.Stop();
            _scrubSeekTimer.Stop();
            IsScrubbing = false;

            if (_wasPlayingBeforeInteraction)
            {
                ResyncAndPlay();
                _wasPlayingBeforeInteraction = false;
            }
            else
            {
                SeekTimeline(CurrentTimelinePosition, isScrubbing: true);
            }
        }

        private bool _isStopRequested;
        private bool _isSeeking = false;


        private long _totalTimelineDurationMs;
        public long TotalTimelineDurationMs
        {
            get => _totalTimelineDurationMs;
            set
            {
                if (SetProperty(ref _totalTimelineDurationMs, value))
                {
                    PlayerViewModel.TotalDuration = value;
                }
            }
        }
        private CancellationTokenSource? _clipUpdateCts;

        private readonly Dictionary<TimelineClipBase, MediaPlayer> _activeVisualClipPlayers = new();
        private readonly Dictionary<TimelineClipBase, MediaPlayer> _activeAudioPlayers = new();

        private readonly DispatcherTimer _timelineTimer;
        private readonly DispatcherTimer _scrubSeekTimer;
        private readonly uint _flatEqIndex;

        private bool _isSyncingPlayers = false; // Prevent reentrant calls to SyncPlayersToTimeline

        private void ScrubSeekTimer_Tick(object? sender, EventArgs e)
        {
            SeekTimeline(CurrentTimelinePosition, isScrubbing: true);
        }



        public bool IsScrubbing
        {
            get => _isScrubbing;
            set => SetProperty(ref _isScrubbing, value);
        }
        private bool _isScrubbing;

        private readonly DispatcherTimer _scrubbingTimer;

        public MainViewModel()
        {
            _scrubbingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _scrubbingTimer.Tick += ScrubbingTimer_Tick;

            _ffmpegExportService = new FFmpegExportService();
            _projectService = new ProjectService();
            PlayerViewModel = new PlayerViewModel();
            VideoList = new VideoListViewModel();
            VideoEditor = new VideoEditorViewModel(this);
            EditorHost = new EditorHostViewModel(PlayerViewModel, VideoEditor);

            var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ggml-large-v3-turbo-q5_0.bin");
            _speechToTextService = new SpeechToTextService(modelPath);

            uint flatPresetIndex = 0;
            using (var eq = new Equalizer())
            {
                for (uint i = 0; i < eq.PresetCount; i++)
                {
                    if (eq.PresetName(i) == "Flat")
                    {
                        flatPresetIndex = i;
                        break;
                    }
                }
            }
            _flatEqIndex = flatPresetIndex;

            VideoEditor.OnClipAdded += MainViewModel_OnClipAdded;
            VideoEditor.ClipInteractionStarted += OnClipInteractionStarted;
            VideoEditor.ClipInteractionEnded += ResumePlaybackIfNeeded;

            PlayerViewModel.PropertyChanged += PlayerViewModel_PropertyChanged;
            PlayerViewModel._libVLC.Log += OnLibVLCLog;

            _performanceWarningTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _performanceWarningTimer.Tick += (s, e) => _performanceWarningCounter = 0; // Reset counter every 10 seconds

            VideoEditor.TimelineClips.CollectionChanged += (s, e) =>
            {
                UIDispatcher.Invoke(() =>
                {
                    if (e.NewItems != null)
                    {
                        foreach (TimelineClipBase newClip in e.NewItems)
                        {
                            newClip.PropertyChanged += Clip_PropertyChanged;
                        }
                    }
                    UpdateTotalTimelineDuration();

                    if (!VideoEditor.TimelineClips.Any())
                    {
                        CurrentTimelinePosition = 0;
                        SyncPlayersToTimeline();
                    }
                });
            };



            PlayPauseTimelineCommand = new RelayCommand(ExecutePlayPauseTimeline);
            StopTimelineCommand = new RelayCommand(ExecuteStopTimeline);
            ExportVideoCommand = new AsyncRelayCommand(StartExportProcessAsync);
            TranscribeVideoCommand = new AsyncRelayCommand(TranscribeVideo);

            SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync);
            LoadProjectCommand = new AsyncRelayCommand(LoadProjectAsync);

            _timelineTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(10)
            };
            _timelineTimer.Tick += OnTimelineTimerTick;

            _scrubSeekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _scrubSeekTimer.Tick += ScrubSeekTimer_Tick;

            UpdateTotalTimelineDuration();
        }

        private async Task SaveProjectAsync()
        {
            var saveFileDialog = new SaveFileDialog
            {
                // 이 부분을 원하는 이름과 확장자로 바꾸세요.
                Filter = "FrameCraft 프로젝트 (*.fcp)|*.fcp",
                Title = "프로젝트 저장하기",
                FileName = "MyProject.fcp"
            };

            if (saveFileDialog.ShowDialog(_mainWindow) != true) return;

            var projectData = new ProjectSaveData
            {
                TimelineClips = new List<TimelineClipBase>(VideoEditor.TimelineClips),
                MediaBin = new List<Myvideo>(VideoList.MyVideoes)
            };

            try
            {
                // 복잡한 로직은 서비스에 위임합니다.
                await _projectService.SaveProjectAsync(projectData, saveFileDialog.FileName);
                StatusMessage = $"프로젝트가 성공적으로 저장되었습니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = "프로젝트 저장 중 오류 발생.";
                MessageBox.Show($"프로젝트 저장에 실패했습니다: {ex.Message}", "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        private async Task LoadProjectAsync()
        {
            var openFileDialog = new OpenFileDialog
            {
                // 이 부분을 원하는 이름과 확장자로 바꾸세요.
                Filter = "FrameCraft 프로젝트 (*.fcp)|*.fcp",
                Title = "프로젝트 열기"
            };

            if (openFileDialog.ShowDialog(_mainWindow) != true) return;

            try
            {
                // 복잡한 로직은 서비스에 위임합니다.
                var projectData = await _projectService.LoadProjectAsync(openFileDialog.FileName);

                if (projectData != null)
                {
                    // 불러온 데이터로 현재 상태를 교체합니다.
                    VideoEditor.TimelineClips.Clear();
                    VideoList.MyVideoes.Clear();

                    foreach (var clip in projectData.TimelineClips) VideoEditor.TimelineClips.Add(clip);
                    foreach (var media in projectData.MediaBin) VideoList.MyVideoes.Add(media);

                    await RecreateThumbnailsAfterLoadAsync();

                    UpdateTotalTimelineDuration();
                    SeekTimeline(0);
                    StatusMessage = $"프로젝트 '{Path.GetFileName(openFileDialog.FileName)}'를 불러왔습니다.";
                }
            }
            catch (Exception ex)
            {
                StatusMessage = "프로젝트 불러오기 중 오류 발생.";
                MessageBox.Show($"프로젝트를 불러오는 데 실패했습니다: {ex.Message}", "불러오기 오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                OnPropertyChanged(nameof(StatusMessage));
            }
        }

        private async Task RecreateThumbnailsAfterLoadAsync()
        {
            foreach (var clip in VideoEditor.TimelineClips)
            {
                if (clip is VideoClip videoClip)
                {
                    videoClip.Thumbnail = await GenerateThumbnailForVideoAsync(videoClip.VideoPath);
                }
                else if (clip is ImageClip imageClip)
                {
                    imageClip.Thumbnail = GenerateThumbnailForImage(imageClip.ImagePath);
                }
            }
        }

        private Task<BitmapImage?> GenerateThumbnailForVideoAsync(string videoPath)
        {
            return Task.Run(() =>
            {
                if (!File.Exists(videoPath)) return null;

                try
                {
                    using (var capture = new VideoCapture(videoPath))
                    {
                        int frameCount = (int)capture.Get(Emgu.CV.CvEnum.CapProp.FrameCount);
                        if (frameCount <= 0) return null;

                        capture.Set(Emgu.CV.CvEnum.CapProp.PosFrames, frameCount / 2);
                        using (var frame = new Mat())
                        {
                            if (!capture.Read(frame) || frame.IsEmpty) return null;

                            using (var bmp = frame.ToBitmap())
                            using (var memory = new MemoryStream())
                            {
                                bmp.Save(memory, ImageFormat.Png);
                                memory.Position = 0;

                                var bitmapImage = new BitmapImage();
                                bitmapImage.BeginInit();
                                bitmapImage.StreamSource = memory;
                                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                                bitmapImage.EndInit();
                                bitmapImage.Freeze(); // UI 스레드 외에서 생성했으므로 Freeze는 필수
                                return bitmapImage;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"썸네일 재생성 실패 ({videoPath}): {ex.Message}");
                    return null;
                }
            });
        }

        private BitmapImage? GenerateThumbnailForImage(string imagePath)
        {
            if (!File.Exists(imagePath)) return null;

            try
            {
                var bitmapImage = new BitmapImage();
                bitmapImage.BeginInit();
                bitmapImage.UriSource = new Uri(imagePath);
                bitmapImage.CacheOption = BitmapCacheOption.OnLoad;
                bitmapImage.EndInit();
                bitmapImage.Freeze();
                return bitmapImage;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"이미지 썸네일 재생성 실패 ({imagePath}): {ex.Message}");
                return null;
            }
        }

        private async Task TranscribeVideo()
        {
            if (IsTimelinePlaying)
            {
                _timelineTimer.Stop();
                PlayerViewModel.PauseAllPlayers();
                IsTimelinePlaying = false;
            }

            var selectedClip = VideoEditor.SelectedClip;

            if (selectedClip == null)
            {
                MessageBox.Show("타임라인에서 비디오 또는 오디오 클립을 선택해주세요.", "클립 선택 필요", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string? mediaPath = null;
            if (selectedClip is VideoClip videoClip)
            {
                mediaPath = videoClip.VideoPath;
            }
            else if (selectedClip is AudioClip audioClip)
            {
                mediaPath = audioClip.AudioPath;
            }
            else
            {
                MessageBox.Show("선택된 클립은 음성 텍스트 변환을 지원하지 않습니다.", "지원되지 않는 클립", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (string.IsNullOrEmpty(mediaPath))
            {
                MessageBox.Show("선택된 클립의 미디어 경로를 찾을 수 없습니다.", "경로 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _transcriptionProgressWindow = new TranscriptionProgressWindow
            {
                DataContext = this,
                Owner = _mainWindow
            };

            if (_mainWindow != null)
            {
                _mainWindow.LocationChanged += OwnerWindow_PositionChanged;
                _mainWindow.SizeChanged += OwnerWindow_PositionChanged;
            }

            _transcriptionProgressWindow.Show();

            selectedClip.IsTranscribing = true;
            IsTranscribing = true;
            StatusMessage = "클립 음성 텍스트 변환 중...";
            OnPropertyChanged(nameof(StatusMessage));

            try
            {
                var progress = new Progress<int>(p => TranscriptionProgress = p);
                var segments = await _speechToTextService.TranscribeAsync(mediaPath, progress);

                // 선택된 클립의 Transcription 속성에 결과 저장
                ObservableCollection<TranscriptionSegment>? targetTranscription = null;
                if (selectedClip is VideoClip vc)
                {
                    targetTranscription = vc.Transcription;
                }
                else if (selectedClip is AudioClip ac)
                {
                    targetTranscription = ac.Transcription;
                }

                if (targetTranscription != null)
                {
                    targetTranscription.Clear();
                    foreach (var segment in segments)
                    {
                        targetTranscription.Add(segment);
                    }
                    selectedClip.IsTranscribed = true;
                    selectedClip.ShowTranscription = true;
                }
                StatusMessage = "클립 음성 텍스트 변환 완료.";
            }
            catch (Exception ex)
            {
                StatusMessage = "클립 음성 텍스트 변환 실패.";
                MessageBox.Show($"클립 음성 텍스트 변환 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                selectedClip.IsTranscribed = false;
            }
            finally
            {
                selectedClip.IsTranscribing = false;
                IsTranscribing = false;
                TranscriptionProgress = 0;
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(VideoEditor));

                if (_mainWindow != null)
                {
                    _mainWindow.LocationChanged -= OwnerWindow_PositionChanged;
                    _mainWindow.SizeChanged -= OwnerWindow_PositionChanged;
                }

                _transcriptionProgressWindow?.Close();
                _transcriptionProgressWindow = null;
            }
        }



        private async Task StartExportProcessAsync()
        {
            _exportCts = new CancellationTokenSource();

            if (_mainWindow == null)
            {
                MessageBox.Show("오류: 메인 윈도우를 찾을 수 없습니다.");
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "MP4 Video (*.mp4)|*.mp4",
                Title = "편집된 영상 저장하기",
                FileName = "output.mp4"
            };

            if (saveFileDialog.ShowDialog(_mainWindow) != true)
            {
                _exportCts.Dispose();
                _exportCts = null;
                return;
            }

            string outputPath = saveFileDialog.FileName;
            var progressViewModel = new ExportProgressViewModel(() => _exportCts.Cancel());
            ExportStarted?.Invoke(this, new ExportStartedEventArgs(progressViewModel));

            try
            {
                bool success = await _ffmpegExportService.ExportVideoAsync(
                    VideoEditor.TimelineClips,
                    TotalTimelineDurationMs / 1000.0,
                    outputPath,
                    progressViewModel,
                    _exportCts.Token);

                if (success)
                {
                    // 성공 시, 진행률 ViewModel의 상태를 '완료'로 변경
                    progressViewModel.StatusMessage = $"성공! 영상이 '{saveFileDialog.FileName}'에 저장되었습니다.";
                    progressViewModel.IsFinished = true;
                }
                else
                {
                    // 실패 또는 취소 시, 진행률 ViewModel의 상태를 '완료'로 변경
                    // (StatusMessage는 이미 서비스에서 설정했음)
                    if (!_exportCts.Token.IsCancellationRequested)
                    {
                        progressViewModel.StatusMessage = $"오류: 렌더링에 실패했습니다."; // 예시
                    }
                    progressViewModel.IsFinished = true;
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"내보내기 중 예외 발생: {ex.Message}";
                progressViewModel.StatusMessage = StatusMessage;
                progressViewModel.IsFinished = true;
            }
            finally
            {
                OnPropertyChanged(nameof(StatusMessage));
                _exportCts.Dispose();
                _exportCts = null;
                IsExporting = false;

                //ExportFinished?.Invoke(this, EventArgs.Empty);
            }
        }


        private void OnTimelineTimerTick(object? sender, EventArgs e)
        {
            if (!IsTimelinePlaying)
            {
                _timelineTimer.Stop();
                return;
            }

            // Stop playback if there are no clips on the timeline
            if (!VideoEditor.TimelineClips.Any())
            {
                ExecuteStopTimeline();
                CurrentTimelinePosition = 0;
                return;
            }

            CurrentTimelinePosition += _timelineTimer.Interval.TotalSeconds;

            // Check for end of timeline FIRST to prevent race conditions.
            if (CurrentTimelinePosition * 1000 >= TotalTimelineDurationMs)
            {
                ExecuteStopTimeline();
                return; // Stop processing this tick.
            }

            // If not the end, then sync players for the new position.
            if (!VideoEditor.IsDraggingClip) SyncPlayersToTimeline();
        }

        private void ResyncAndPlay()
        {
            // 1. Ensure everything is paused and the timeline isn't running.
            _timelineTimer.Stop();
            PlayerViewModel.PauseAllPlayers();
            IsTimelinePlaying = false; // Set to false temporarily

            // 2. Seek all active players to the correct time while they are paused.
            SyncPlayersToTimeline();

            // 3. Now that everyone is at the same starting line, start the race.
            IsTimelinePlaying = true; // Set to true for the playback state
            PlayerViewModel.ResumeAllPlayers(); // Use a general resume command
            _timelineTimer.Start();
        }

        private void Clip_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TimelineClipBase.StartPosition) || e.PropertyName == nameof(TimelineClipBase.Duration))
            {
                // 타임라인 총 길이는 드래그/스크럽 중에도 항상 즉시 반영
                UpdateTotalTimelineDuration();

                // 드래그/스크럽 중에는 무거운 재동기화만 건너뜀
                if (VideoEditor.IsDraggingClip || IsScrubbing) return;

                // StartPosition이 변경되고, 클립이 현재 활성 상태인 경우에만 재동기화.
                if (sender is TimelineClipBase changedClip)
                {
                    if (CurrentTimelinePosition >= changedClip.StartPosition &&
                        CurrentTimelinePosition < (changedClip.StartPosition + changedClip.Duration))
                    {
                        SyncPlayersToTimeline();
                    }
                }
            }
            else if (e.PropertyName == nameof(TimelineClipBase.TrackIndex))
            {
                // TrackIndex가 변경되면 비디오 Z-order를 다시 적용
                if (sender is VideoClip)
                {
                    VideoClipZOrderChanged?.Invoke(this, EventArgs.Empty);
                    // 드래그/스크럽 중에는 무거운 동기화 생략
                    if (VideoEditor.IsDraggingClip || IsScrubbing) return;
                    SyncPlayersToTimeline();
                }
            }
            else if (e.PropertyName == nameof(TimelineClipBase.X) || e.PropertyName == nameof(TimelineClipBase.Y))
            {
                // X, Y 위치가 변경되면 비디오 clipping을 즉시 업데이트
                // 재생 중 드래그 시 비디오가 UI 위로 나타나는 것을 방지
                if (sender is VideoClip)
                {
                    // Force immediate clipping update without waiting for timer
                    VideoClipZOrderChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (e.PropertyName == nameof(TimelineClipBase.Volume))
            {
                if (sender is TimelineClipBase changedClip && (changedClip is VideoClip || changedClip is AudioClip))
                {
                    if (_activeAudioPlayers.TryGetValue(changedClip, out var player))
                    {
                        int combinedVolume = (int)((changedClip.Volume / 100.0) * (PlayerViewModel.Volume / 100.0) * 100);
                        var preampDb = ConvertVolumeToDb(combinedVolume);
                        Console.WriteLine($"[Clip Vol Change] Clip: '{changedClip.Name}', Combined Vol: {combinedVolume}, Preamp: {preampDb:F2} dB");

                        using var newEqualizer = new Equalizer(_flatEqIndex);
                        newEqualizer.SetPreamp(preampDb);
                        player.SetEqualizer(newEqualizer);
                    }
                }
            }
            else if (e.PropertyName == nameof(TimelineClipBase.SpeedRatio))
            {
                if (sender is TimelineClipBase changedClip)
                {
                    // When speed changes, the duration changes, which might affect which clips are active.
                    // A full resync is the safest way to ensure everything is correct.
                    SyncPlayersToTimeline();
                    UpdateTotalTimelineDuration();
                }
            }
            else if (e.PropertyName == nameof(TimelineClipBase.IsMuted))
            {
                // 변경된 클립이 현재 재생 위치에 활성화된 클립이라면
                if (sender is TimelineClipBase changedClip &&
                    CurrentTimelinePosition >= changedClip.StartPosition &&
                    CurrentTimelinePosition < (changedClip.StartPosition + changedClip.Duration))
                {
                    // 오디오 플레이어 상태를 즉시 재동기화합니다.
                    SyncPlayersToTimeline();
                }
            }
        }

        private void PlayerViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayerViewModel.Volume))
            {
                Console.WriteLine($"\n[Master Volume Changed] Master Vol: {PlayerViewModel.Volume}");
                foreach (var (clip, player) in _activeAudioPlayers)
                {
                    int combinedVolume = (int)((clip.Volume / 100.0) * (PlayerViewModel.Volume / 100.0) * 100);
                    var preampDb = ConvertVolumeToDb(combinedVolume);
                    Console.WriteLine($"  -> Clip: '{clip.Name}', Combined Vol: {combinedVolume}, Preamp: {preampDb:F2} dB");

                    using var newEqualizer = new Equalizer(_flatEqIndex);
                    newEqualizer.SetPreamp(preampDb);
                    player.SetEqualizer(newEqualizer);
                }
            }
        }

        private void MainViewModel_OnClipAdded(object? sender, ClipAddedEventArgs e)
        {
            if (VideoEditor.TimelineClips.Count == 1)
            {
                SyncPlayersToTimeline();
            }
        }

        private bool _wasPlayingBeforeInteraction = false;

        public void ResumePlaybackIfNeeded()
        {
            if (_wasPlayingBeforeInteraction)
            {
                _wasPlayingBeforeInteraction = false;

                // Use Dispatcher to avoid blocking the current thread
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // Use ResyncAndPlay to ensure proper Z-order and player state
                        ResyncAndPlay();
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[RESUME ERROR] Error resuming playback: {ex.Message}");
                    }
                }, System.Windows.Threading.DispatcherPriority.Normal);
            }
        }

        private void OnClipInteractionStarted()
        {
            // 드래그 시작 시에만 일시정지 플래그 저장 및 타이머/플레이어 일시정지
            _wasPlayingBeforeInteraction = IsTimelinePlaying;
            if (IsTimelinePlaying)
            {
                _timelineTimer.Stop();
                PlayerViewModel.PauseAllPlayers();
                IsTimelinePlaying = false;
            }
        }

        public void StopPlayback()
        {
            _wasPlayingBeforeInteraction = IsTimelinePlaying;
            if (IsTimelinePlaying)
            {
                _timelineTimer.Stop();
                PlayerViewModel.PauseAllPlayers();
                IsTimelinePlaying = false;
            }
        }

        private void ExecutePlayPauseTimeline()
        {
            // Don't allow playback if there are no clips on the timeline
            if (!VideoEditor.TimelineClips.Any())
            {
                return;
            }

            if (IsTimelinePlaying)
            {
                _timelineTimer.Stop();
                PlayerViewModel.PauseAllPlayers();
                IsTimelinePlaying = false;
            }
            else
            {
                ResyncAndPlay();
            }
        }

        private void ExecuteStopTimeline()
        {
            Debug.WriteLine("[COMMAND] Stop 버튼 클릭.");
            _timelineTimer.Stop();
            PlayerViewModel.Stop();

            _activeVisualClipPlayers.Clear();
            _activeAudioPlayers.Clear();

            ActiveVideoClips.Clear();
            ActiveWpfOverlays.Clear();

            CurrentTimelinePosition = 0;
            IsTimelinePlaying = false;
        }

        public void SeekTimeline(double timeSec, bool isScrubbing = false)
        {
            CurrentTimelinePosition = timeSec;
            SyncPlayersToTimeline();
        }

        public void SyncPlayersToTimeline()
        {
            // Prevent reentrant calls - if already syncing, skip this call
            if (_isSyncingPlayers)
            {
                Debug.WriteLine("[SYNC] SyncPlayersToTimeline skipped - already syncing");
                return;
            }

            _isSyncingPlayers = true;

            try
            {
                bool hasClips = VideoEditor.TimelineClips.Any();
                PlayerViewModel.IsControlBarVisible = hasClips;
                PlayerViewModel.VideoViewBackground = hasClips ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black) : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#525252"));

                var activeClipsAtCurrentTime = VideoEditor.TimelineClips
                    .Where(c => c.StartPosition <= CurrentTimelinePosition && (c.StartPosition + c.Duration) > CurrentTimelinePosition)
                    .ToList();

                // --- Separate clips by type ---
                var activeVideoClips = activeClipsAtCurrentTime.OfType<VideoClip>().OrderBy(c => c.TrackIndex).ToList();
                var activeWpfOverlays = activeClipsAtCurrentTime.Where(c => c is ImageClip || c is TextClip).OrderBy(c => c.TrackIndex).ToList();

                // --- Video Clips Management ---
                var videoClipsToDeactivate = ActiveVideoClips.Except(activeVideoClips).ToList();
                foreach (var clip in videoClipsToDeactivate)
                {
                    if (clip is VideoClip videoClip && videoClip.PlayerInstance != null)
                    {
                        videoClip.PlayerInstance.Stop();
                        videoClip.PlayerInstance.Media = null;
                        videoClip.PlayerInstance = null;
                    }
                    ActiveVideoClips.Remove(clip);
                }

                foreach (var videoClip in activeVideoClips)
                {
                    if (!ActiveVideoClips.Contains(videoClip))
                    {
                        var availablePlayer = PlayerViewModel.VideoPlayers.FirstOrDefault(p => p.Media == null);
                        if (availablePlayer != null)
                        {
                            videoClip.PlayerInstance = availablePlayer;
                            double timeWithinClip = CurrentTimelinePosition - videoClip.StartPosition;
                            if (!string.IsNullOrEmpty(videoClip.VideoPath))
                            {
                                // FIXED: Include SourceStartTime in the seek position
                                double seekTime = videoClip.SourceStartTime + (timeWithinClip * videoClip.SpeedRatio);
                                var media = PlayerViewModel.PrepareMedia(videoClip.VideoPath, seekTime, videoOnly: true, audioOnly: false);
                                videoClip.PlayerInstance.Media = media;
                                videoClip.PlayerInstance.SetRate((float)videoClip.SpeedRatio);

                                // Pre-warm the player to initialize resources before adding to UI
                                if (videoClip.PlayerInstance.Play())
                                {
                                    videoClip.PlayerInstance.SetPause(true);
                                }
                            }
                        }
                        ActiveVideoClips.Add(videoClip);
                    }
                }

                // --- WPF Overlays Management ---
                var wpfOverlaysToDeactivate = ActiveWpfOverlays.Except(activeWpfOverlays).ToList();
                foreach (var clip in wpfOverlaysToDeactivate) { ActiveWpfOverlays.Remove(clip); }

                foreach (var clip in activeWpfOverlays)
                {
                    if (!ActiveWpfOverlays.Contains(clip)) { ActiveWpfOverlays.Add(clip); }
                }

                // --- Update Active Players (Playback state) ---
                bool playbackStateChanged = false;
                foreach (var videoClip in activeVideoClips)
                {
                    if (videoClip.PlayerInstance is MediaPlayer player)
                    {
                        bool wasPlaying = player.IsPlaying;
                        player.SetRate((float)videoClip.SpeedRatio);
                        double timeWithinClip = CurrentTimelinePosition - videoClip.StartPosition;
                        if (IsScrubbing || VideoEditor.IsDraggingClip)
                        {
                            // FIXED: Include SourceStartTime when seeking during scrubbing
                            double seekTime = videoClip.SourceStartTime + (timeWithinClip * videoClip.SpeedRatio);
                            player.Time = (long)(seekTime * 1000);
                            if (!player.IsPlaying) player.Play();
                            player.SetPause(true);
                        }
                        else if (IsTimelinePlaying && !player.IsPlaying)
                        {
                            player.Play();
                            if (!wasPlaying) playbackStateChanged = true;
                        }
                        else if (!IsTimelinePlaying && player.IsPlaying)
                        {
                            player.SetPause(true);
                            playbackStateChanged = true;
                        }
                    }
                }

                // Force Z-order update when playback state changes to ensure proper layering
                if (playbackStateChanged)
                {
                    VideoClipZOrderChanged?.Invoke(this, EventArgs.Empty);
                }

                // --- Audio Clips Management (Largely unchanged) ---
                var activeAudioSourceClips = activeClipsAtCurrentTime
                    .Where(c => !c.IsMuted && (c is VideoClip || c is AudioClip))
                    .ToList();
                var audioClipsToDeactivate = _activeAudioPlayers.Keys.Except(activeAudioSourceClips).ToList();

                foreach (var clip in audioClipsToDeactivate)
                {
                    if (_activeAudioPlayers.Remove(clip, out var player)) { player.Stop(); player.Media?.Dispose(); player.Media = null; }
                }

                foreach (var clip in activeAudioSourceClips)
                {
                    if (!_activeAudioPlayers.TryGetValue(clip, out var player))
                    {
                        player = PlayerViewModel.GetAvailableAudioPlayer();
                        if (player == null) continue;
                        _activeAudioPlayers.Add(clip, player);

                        string mediaPath = (clip is VideoClip v) ? v.VideoPath : (clip as AudioClip)?.AudioPath ?? string.Empty;
                        if (!string.IsNullOrEmpty(mediaPath))
                        {
                            using var equalizer = new Equalizer(_flatEqIndex);
                            int combinedVolume = (int)((clip.Volume / 100.0) * (PlayerViewModel.Volume / 100.0) * 100);
                            equalizer.SetPreamp(ConvertVolumeToDb(combinedVolume));
                            player.SetEqualizer(equalizer);
                            double sourceStartTime = (clip is VideoClip vc) ? vc.SourceStartTime : (clip as AudioClip)?.SourceStartTime ?? 0;
                            double timeWithinClip = CurrentTimelinePosition - clip.StartPosition;
                            player.Media = PlayerViewModel.PrepareMedia(mediaPath, sourceStartTime + (timeWithinClip * clip.SpeedRatio), videoOnly: false, audioOnly: true);
                            player.SetRate((float)clip.SpeedRatio);
                        }
                    }

                    if (player != null)
                    {
                        player.SetRate((float)clip.SpeedRatio);
                        if (IsScrubbing || VideoEditor.IsDraggingClip)
                        {
                            double sourceStartTime = (clip is VideoClip vc) ? vc.SourceStartTime : (clip as AudioClip)?.SourceStartTime ?? 0;
                            double timeWithinClip = CurrentTimelinePosition - clip.StartPosition;
                            player.Time = (long)((sourceStartTime + (timeWithinClip * clip.SpeedRatio)) * 1000);
                            if (!player.IsPlaying) player.Play();
                            player.SetPause(true);
                        }
                        else if (IsTimelinePlaying && !player.IsPlaying) { player.Play(); }
                        else if (!IsTimelinePlaying && player.IsPlaying) { player.SetPause(true); }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SYNC ERROR] SyncPlayersToTimeline error: {ex.Message}");
            }
            finally
            {
                _isSyncingPlayers = false;
            }
        }

        private float ConvertVolumeToDb(int volume)
        {
            if (volume <= 0) return -20.0f;
            if (volume >= 100) return 0.0f;

            double linearValue = volume / 100.0;
            float db = (float)(20 * Math.Log10(linearValue));

            return Math.Max(-20.0f, db);
        }

        private async void OnClipFinished(object? sender, EventArgs e)
        {
            if (VideoEditor.CurrentlyPlayingClip != null)
            {
                Debug.WriteLine($"'{VideoEditor.CurrentlyPlayingClip.Name}' 클립 재생 완료. 마스터 루프가 계속 진행");
            }
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
        }

        private void OwnerWindow_PositionChanged(object? sender, EventArgs e)
        {
            if (_transcriptionProgressWindow != null && _mainWindow != null)
            {
                _transcriptionProgressWindow.Left = _mainWindow.Left + (_mainWindow.Width - _transcriptionProgressWindow.Width) / 2;
                _transcriptionProgressWindow.Top = _mainWindow.Top + (_mainWindow.Height - _transcriptionProgressWindow.Height) / 2;
            }
        }

        public void TriggerVideoClipZOrderUpdate()
        {
            // Trigger Z-order update by raising the event
            // This will set _needsZOrderUpdate flag in MainWindow which will
            // apply proper Z-ordering to HwndHost windows
            VideoClipZOrderChanged?.Invoke(this, EventArgs.Empty);
        }

        private void UpdateClipsForPlayerSizeChange()
        {
            if (PlayerHostWidth <= 1 || PlayerHostHeight <= 1) return;
            if (_previousPlayerHostWidth <= 1 || _previousPlayerHostHeight <= 1)
            {
                // First time initialization
                _previousPlayerHostWidth = PlayerHostWidth;
                _previousPlayerHostHeight = PlayerHostHeight;
            }

            const double controlBarHeight = 50;
            double availableVideoHeight = PlayerHostHeight - controlBarHeight;
            double previousAvailableVideoHeight = _previousPlayerHostHeight - controlBarHeight;

            // Calculate scale ratios
            double widthRatio = PlayerHostWidth / _previousPlayerHostWidth;
            double heightRatio = availableVideoHeight / previousAvailableVideoHeight;

            foreach (var clip in VideoEditor.TimelineClips)
            {
                if (clip is VideoClip videoClip)
                {
                    UpdateVideoClipLayout(videoClip, availableVideoHeight, widthRatio, heightRatio);
                }
                else if (clip is ImageClip imageClip)
                {
                    UpdateImageClipLayout(imageClip, availableVideoHeight, widthRatio, heightRatio);
                }
            }
        }

        private void UpdateVideoClipLayout(VideoClip videoClip, double availableVideoHeight, double widthRatio, double heightRatio)
        {
            if (videoClip.SourceWidth <= 0 || videoClip.SourceHeight <= 0) return;

            // If user has positioned the clip, scale its position and size proportionally
            if (videoClip.IsUserPositioned)
            {
                // Scale position
                videoClip.X *= widthRatio;
                videoClip.Y *= heightRatio;

                // Scale size
                videoClip.RenderWidth *= widthRatio;
                videoClip.RenderHeight *= heightRatio;
                return;
            }

            // For clips not yet positioned by user, use default centered layout
            double playerAspectRatio = PlayerHostWidth / availableVideoHeight;
            double videoAspectRatio = (double)videoClip.SourceWidth / videoClip.SourceHeight;

            double renderWidth, renderHeight, x, y;

            if (playerAspectRatio > videoAspectRatio)
            {
                renderHeight = availableVideoHeight;
                renderWidth = renderHeight * videoAspectRatio;
            }
            else
            {
                renderWidth = PlayerHostWidth;
                renderHeight = renderWidth / videoAspectRatio;
            }

            x = (PlayerHostWidth - renderWidth) / 2;
            y = (availableVideoHeight - renderHeight) / 2;

            videoClip.X = x;
            videoClip.Y = y;
            videoClip.RenderWidth = renderWidth;
            videoClip.RenderHeight = renderHeight;

            // Mark initial layout as complete
            videoClip.MarkInitialLayoutComplete();
        }

        private void UpdateImageClipLayout(ImageClip imageClip, double availableVideoHeight, double widthRatio, double heightRatio)
        {
            if (imageClip.SourceWidth <= 0 || imageClip.SourceHeight <= 0) return;

            // If user has positioned the clip, scale its position and size proportionally
            if (imageClip.IsUserPositioned)
            {
                // Scale position
                imageClip.X *= widthRatio;
                imageClip.Y *= heightRatio;

                // Scale size
                imageClip.RenderWidth *= widthRatio;
                imageClip.RenderHeight *= heightRatio;
                return;
            }

            // For clips not yet positioned by user, use default centered layout
            double playerAspectRatio = PlayerHostWidth / availableVideoHeight;
            double imageAspectRatio = (double)imageClip.SourceWidth / imageClip.SourceHeight;

            double renderWidth, renderHeight, x, y;

            if (playerAspectRatio > imageAspectRatio)
            {
                renderHeight = availableVideoHeight;
                renderWidth = renderHeight * imageAspectRatio;
            }
            else
            {
                renderWidth = PlayerHostWidth;
                renderHeight = renderWidth / imageAspectRatio;
            }

            x = (PlayerHostWidth - renderWidth) / 2;
            y = (availableVideoHeight - renderHeight) / 2;

            imageClip.X = x;
            imageClip.Y = y;
            imageClip.RenderWidth = renderWidth;
            imageClip.RenderHeight = renderHeight;

            // Mark initial layout as complete
            imageClip.MarkInitialLayoutComplete();
        }

        public void Dispose()
        {
            // Unsubscribe from LibVLC log BEFORE disposing PlayerViewModel (which disposes _libVLC)
            if (PlayerViewModel != null && PlayerViewModel._libVLC != null)
            {
                PlayerViewModel._libVLC.Log -= OnLibVLCLog;
            }

            PlayerViewModel?.Dispose();
            VideoEditor?.Dispose();

            if (VideoEditor != null)
            {
                VideoEditor.OnClipAdded -= MainViewModel_OnClipAdded;
            }
        }

        private void OnLibVLCLog(object? sender, LogEventArgs e)
        {
            if (e.Level >= LogLevel.Warning && e.Message.Contains("computer too slow"))
            {
                _performanceWarningCounter++;
                if (!_performanceWarningTimer.IsEnabled)
                {
                    _performanceWarningTimer.Start();
                }

                // If we get more than 5 warnings in 10 seconds, show the message.
                if (_performanceWarningCounter > 5)
                {
                    ShowPerformanceWarning("재생 성능이 저하되었습니다. 프록시 미디어 사용을 고려해보세요.");
                    _performanceWarningCounter = 0; // Reset after showing
                    _performanceWarningTimer.Stop();
                }
            }
        }

        private async void ShowPerformanceWarning(string message)
        {
            PerformanceWarningMessage = message;
            IsPerformanceWarningVisible = true;
            await Task.Delay(5000); // Show the message for 5 seconds
            IsPerformanceWarningVisible = false;
        }
    }
}