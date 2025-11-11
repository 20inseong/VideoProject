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
        public event Action? AnalysisCompleted;

        private readonly ProjectService _projectService;
        private readonly FFmpegExportService _ffmpegExportService;
        private readonly EmotionDetect _emotionDetect;

        public PlayerViewModel PlayerViewModel { get; }
        public VideoListViewModel VideoList { get; }
        public VideoEditorViewModel VideoEditor { get; }
        public EditorHostViewModel EditorHost { get; }
        public string StatusMessage { get; set; } = "준비 완료";
        public IAsyncRelayCommand ExportVideoCommand { get; }
        public IAsyncRelayCommand TranscribeVideoCommand { get; }
        public IAsyncRelayCommand SaveProjectCommand { get; }
        public IAsyncRelayCommand LoadProjectCommand { get; }
        public IAsyncRelayCommand AnalyzeEmotionCommand { get; }
        public IRelayCommand<double> SeekToTimestampCommand { get; }
        public IRelayCommand<string> SeekFramesCommand { get; }
        public IRelayCommand<string> SeekSecondsCommand { get; }
        public IRelayCommand<string> SeekToClipEdgeCommand { get; }

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

        // Track when a video clip is being dragged in preview (to hide WPF overlays temporarily)
        private bool _isVideoClipBeingDraggedInPreview = false;
        private List<TimelineClipBase>? _wpfOverlaysHiddenDuringVideoDrag = null;



        private double _playerHostWidth = 1;
        private double _previousPlayerHostWidth = 1;
        private double _referencePreviewWidth = 1;  // Fixed 16:9 preview reference width
        private double _referencePreviewHeight = 1; // Fixed 16:9 preview reference height
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

        private int _analysisProgress;
        public int AnalysisProgress
        {
            get => _analysisProgress;
            set => SetProperty(ref _analysisProgress, value);
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
            //_emotionDetectService = new EmotionDetectTestDataService();
            string pythonExePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "Test", "python.exe");
            string pythonScriptPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Common", "EmotionDetect.py");
            _emotionDetect = new EmotionDetect(pythonExePath, pythonScriptPath);

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
                            
                            // Initialize layout for the new clip if preview size has been established
                            if (_referencePreviewWidth > 1 && _referencePreviewHeight > 1)
                            {
                                if (newClip is VideoClip videoClip && !videoClip.IsUserPositioned)
                                {
                                    InitializeVideoClipLayout(videoClip, _referencePreviewWidth, _referencePreviewHeight);
                                }
                                else if (newClip is ImageClip imageClip && !imageClip.IsUserPositioned)
                                {
                                    InitializeImageClipLayout(imageClip, _referencePreviewWidth, _referencePreviewHeight);
                                }
                                else if (newClip is TextClip textClip && !textClip.IsUserPositioned)
                                {
                                    InitializeTextClipLayout(textClip, _referencePreviewWidth, _referencePreviewHeight);
                                }
                            }
                        }
                    }
                    
                    if (e.OldItems != null)
                    {
                        foreach (TimelineClipBase oldClip in e.OldItems)
                        {
                            oldClip.PropertyChanged -= Clip_PropertyChanged;
                        }
                    }
                    
                    UpdateTotalTimelineDuration();

                    // Always refresh preview when clips are added or removed
                    if (!VideoEditor.TimelineClips.Any())
                    {
                        CurrentTimelinePosition = 0;
                    }
                    
                    // Refresh the preview to show updated timeline
                    SyncPlayersToTimeline();

                    PlayPauseTimelineCommand.NotifyCanExecuteChanged();
                    StopTimelineCommand.NotifyCanExecuteChanged();
                    TranscribeVideoCommand.NotifyCanExecuteChanged();
                    SeekFramesCommand.NotifyCanExecuteChanged();
                    SeekSecondsCommand.NotifyCanExecuteChanged();
                    SeekToClipEdgeCommand.NotifyCanExecuteChanged();
                });
            };



            PlayPauseTimelineCommand = new RelayCommand(ExecutePlayPauseTimeline, CanExecuteTimelineCommands);
            StopTimelineCommand = new RelayCommand(ExecuteStopTimeline, CanExecuteTimelineCommands);
            ExportVideoCommand = new AsyncRelayCommand(StartExportProcessAsync);
            TranscribeVideoCommand = new AsyncRelayCommand(TranscribeVideo, () => CanExecuteTimelineCommands() && !IsTranscribing);

            AnalyzeEmotionCommand = new AsyncRelayCommand(AnalyzeEmotionAsync, CanAnalyzeEmotion);
            SeekToTimestampCommand = new RelayCommand<double>(SeekToTimestamp);

            SaveProjectCommand = new AsyncRelayCommand(SaveProjectAsync);
            LoadProjectCommand = new AsyncRelayCommand(LoadProjectAsync);

            SeekFramesCommand = new RelayCommand<string>(ExecuteSeekFrames, _ => CanExecuteTimelineCommands());
            SeekSecondsCommand = new RelayCommand<string>(ExecuteSeekSeconds, _ => CanExecuteTimelineCommands());
            SeekToClipEdgeCommand = new RelayCommand<string>(ExecuteSeekToClipEdge, _ => CanExecuteTimelineCommands());

            _timelineTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(10)
            };
            _timelineTimer.Tick += OnTimelineTimerTick;

            _scrubSeekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _scrubSeekTimer.Tick += ScrubSeekTimer_Tick;

            UpdateTotalTimelineDuration();
        }

        private bool CanExecuteTimelineCommands()
        {
            // VideoEditor의 TimelineClips에 항목이 하나라도 있는지 확인
            return VideoEditor.TimelineClips.Any();
        }

        private void ExecuteSeekFrames(string? direction)
        {
            if (!double.TryParse(direction, out double frameDirection)) return;
            const double frameRate = 30.0; // 30fps로 가정
            double timeChange = frameDirection / frameRate;
            double newPosition = Math.Max(0, CurrentTimelinePosition + timeChange);
            SeekTimeline(newPosition);
        }

        private void ExecuteSeekSeconds(string? direction)
        {
            if (!double.TryParse(direction, out double seconds)) return;
            double newPosition = Math.Max(0, CurrentTimelinePosition + seconds);
            SeekTimeline(newPosition);
        }

        private void ExecuteSeekToClipEdge(string? direction)
        {
            if (!VideoEditor.TimelineClips.Any()) return;

            double targetTime;

            if (direction == "next")
            {
                // 현재 위치 바로 다음 클립의 시작점 찾기
                var nextClip = VideoEditor.TimelineClips
                    .OrderBy(c => c.StartPosition)
                    .FirstOrDefault(c => c.StartPosition > CurrentTimelinePosition + 0.01); // 현재 클립을 피하기 위해 작은 값 추가

                targetTime = nextClip?.StartPosition ?? TotalTimelineDurationMs / 1000.0;
            }
            else // "prev"
            {
                // 현재 위치 바로 이전 클립의 시작점 찾기
                var prevClip = VideoEditor.TimelineClips
                    .OrderByDescending(c => c.StartPosition)
                    .FirstOrDefault(c => c.StartPosition < CurrentTimelinePosition - 0.01);

                targetTime = prevClip?.StartPosition ?? 0;
            }

            SeekTimeline(targetTime);
        }

        private void SeekToTimestamp(double timeInSeconds)
        {
            Debug.WriteLine($"[SeekToTimestamp] Received time: {timeInSeconds}");
            Debug.WriteLine($"[SeekToTimestamp] Before - CurrentTimelinePosition: {CurrentTimelinePosition}");

            SeekTimeline(timeInSeconds);

            Debug.WriteLine($"[SeekToTimestamp] After - CurrentTimelinePosition: {CurrentTimelinePosition}");
        }

        private bool CanAnalyzeEmotion()
        {
            return VideoEditor.SelectedClip is VideoClip clip &&
                   !clip.IsEmotionAnalyzed &&
                   !clip.IsAnalyzingEmotion;
        }

        private async Task AnalyzeEmotionAsync()
        {
            if (VideoEditor.SelectedClip is not VideoClip selectedClip) return;

            selectedClip.IsAnalyzingEmotion = true;
            AnalyzeEmotionCommand.NotifyCanExecuteChanged();
            StatusMessage = "클립 감정 분석 중...";
            AnalysisProgress = 0;
            OnPropertyChanged(nameof(StatusMessage));
            AnalyzeEmotionCommand.NotifyCanExecuteChanged(); // 버튼 비활성화를 위해 상태 변경 즉시 알림

            // 분석 중 다른 UI 조작을 막기 위해 오버레이 등을 숨깁니다.
            HidePreviewObjectsForModal();

            try
            {
                string? videoPath = selectedClip.VideoPath;
                double videoDuration = selectedClip.Duration;
                const int fps = 30; // 초당 30프레임 기준
                const int frameInterval = 120; // 120프레임마다(즉, 4초마다) 1프레임 추출

                // 1단계: 프레임 추출
                StatusMessage = "분석을 위한 프레임 추출 중...";
                OnPropertyChanged(nameof(StatusMessage));

                // 임시 폴더를 초기화합니다. (이 로직은 EmotionDetect 클래스로 옮겨도 좋습니다.)
                var tempFrameFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TempFramesDebug");
                if (Directory.Exists(tempFrameFolder)) Directory.Delete(tempFrameFolder, true);
                Directory.CreateDirectory(tempFrameFolder);

                double intervalSeconds = frameInterval / (double)fps;
                if (intervalSeconds <= 0) intervalSeconds = 1; // 0으로 나누는 오류 방지

                int totalSteps = (int)(videoDuration / intervalSeconds);

                for (int step = 0; step <= totalSteps; step++)
                {
                    double timestamp = step * intervalSeconds;
                    await _emotionDetect.ExtractFrameAsync(videoPath, timestamp);

                    // 프레임 추출 진행률을 0% ~ 50% 범위로 계산하여 UI에 업데이트
                    AnalysisProgress = (int)((double)step / totalSteps * 50);
                }
                Debug.WriteLine("[Emotion Analysis] Frame extraction complete.");

                // 2단계: Python AI 모델 실행
                StatusMessage = "AI 모델로 감정 분석 중...";
                OnPropertyChanged(nameof(StatusMessage));

                var pythonProgress = new Progress<int>(percent =>
                {
                    // Python에서 받은 진행률(p)을 50~100 범위로 스케일링하여 업데이트
                    AnalysisProgress = 50 + (int)(percent / 2.0);
                });

                Debug.WriteLine("[Emotion Analysis] Starting Python script...");
                var analysisResults = await _emotionDetect.RunPythonEmotionDetectionAsync(selectedClip.Name, pythonProgress);

                if (analysisResults == null)
                {
                    throw new Exception("감정 분석 결과를 받지 못했습니다. Python 스크립트 실행 로그를 확인하세요.");
                }

                // 3단계: 결과 처리
                selectedClip.EmotionAnalysisResults.Clear();
                foreach (var result in analysisResults)
                {
                    selectedClip.EmotionAnalysisResults.Add(result);
                }

                AnalysisProgress = 100;
                selectedClip.IsEmotionAnalyzed = true;
                selectedClip.ShowEmotionAnalysis = true; // 결과를 바로 표시
                StatusMessage = "클립 감정 분석 완료.";
            }
            catch (Exception ex)
            {
                StatusMessage = "클립 감정 분석 중 오류 발생.";
                MessageBox.Show($"감정 분석 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                selectedClip.IsEmotionAnalyzed = false;
            }
            finally
            {
                selectedClip.IsAnalyzingEmotion = false;
                AnalysisProgress = 0; // 작업이 끝나면 진행률 초기화
                OnPropertyChanged(nameof(StatusMessage));
                AnalyzeEmotionCommand.NotifyCanExecuteChanged(); // 버튼 상태 최종 갱신
                RestorePreviewObjectsAfterModal();

                AnalysisCompleted?.Invoke();
            }
        }

        public void HidePreviewObjectsForModal()
        {
            if (_savedActiveVideoClips == null)
                _savedActiveVideoClips = new List<TimelineClipBase>(ActiveVideoClips);
            if (_savedActiveWpfOverlays == null)
                _savedActiveWpfOverlays = new List<TimelineClipBase>(ActiveWpfOverlays);

            ActiveVideoClips.Clear();
            ActiveWpfOverlays.Clear();

            var overlayWindow = _mainWindow?.OwnedWindows.OfType<Views.OverlayWindow>().FirstOrDefault();
            if (overlayWindow != null && overlayWindow.IsVisible)
            {
                _wasOverlayVisible = true;
                overlayWindow.Hide();
            }
        }

        public void RestorePreviewObjectsAfterModal()
        {
            // Do NOT restore ActiveVideoClips directly; force a clean rebuild to avoid stale MediaPlayer handles
            _savedActiveVideoClips = null;

            if (_savedActiveWpfOverlays != null)
            {
                foreach (var clip in _savedActiveWpfOverlays)
                    ActiveWpfOverlays.Add(clip);
                _savedActiveWpfOverlays = null;
            }
            var overlayWindow = _mainWindow?.OwnedWindows.OfType<Views.OverlayWindow>().FirstOrDefault();
            if (overlayWindow != null && _wasOverlayVisible)
            {
                overlayWindow.Show();
                _wasOverlayVisible = false;
            }
            UIDispatcher.InvokeAsync(async () =>
            {
                // Full re-sync: stop, clear view list, rebuild active players and views
                PlayerViewModel.Stop();
                _activeVisualClipPlayers.Clear();
                _activeAudioPlayers.Clear();
                ActiveVideoClips.Clear();
                await Task.Delay(100);
                SyncPlayersToTimeline();
            });
        }

        private async Task SaveProjectAsync()
        {
            bool isProjectEmpty = !VideoEditor.TimelineClips.Any() && !VideoList.MyVideoes.Any();

            if (isProjectEmpty)
            {
                HidePreviewObjectsForModal();
                var result = MessageBox.Show(
                    "프로젝트에 추가된 미디어나 타임라인 클립이 없습니다. 그래도 저장하시겠습니까?",
                    "빈 프로젝트 저장 확인",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question
                );
                RestorePreviewObjectsAfterModal();

                if (result == MessageBoxResult.No)
                {
                    StatusMessage = "프로젝트 저장이 취소되었습니다.";
                    OnPropertyChanged(nameof(StatusMessage));
                    return;
                }
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "FrameCraft 프로젝트 (*.fcp)|*.fcp",
                Title = "프로젝트 저장하기",
                FileName = "MyProject.fcp"
            };

            HidePreviewObjectsForModal();
            bool? dialogResult = saveFileDialog.ShowDialog(_mainWindow);
            RestorePreviewObjectsAfterModal();

            if (dialogResult != true) return;

            var projectData = new ProjectSaveData
            {
                TimelineClips = new List<TimelineClipBase>(VideoEditor.TimelineClips),
                MediaBin = new List<Myvideo>(VideoList.MyVideoes)
            };

            try
            {
                await _projectService.SaveProjectAsync(projectData, saveFileDialog.FileName);
                StatusMessage = $"프로젝트가 성공적으로 저장되었습니다.";
            }
            catch (Exception ex)
            {
                StatusMessage = "프로젝트 저장 중 오류 발생.";
                HidePreviewObjectsForModal();
                MessageBox.Show($"프로젝트 저장에 실패했습니다: {ex.Message}", "저장 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                RestorePreviewObjectsAfterModal();
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
                Filter = "FrameCraft 프로젝트 (*.fcp)|*.fcp",
                Title = "프로젝트 열기"
            };

            HidePreviewObjectsForModal();
            bool? dialogResult = openFileDialog.ShowDialog(_mainWindow);
            RestorePreviewObjectsAfterModal();

            if (dialogResult != true) return;

            try
            {
                var projectData = await _projectService.LoadProjectAsync(openFileDialog.FileName);

                if (projectData != null)
                {
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
                HidePreviewObjectsForModal();
                MessageBox.Show($"프로젝트를 불러오는 데 실패했습니다: {ex.Message}", "불러오기 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                RestorePreviewObjectsAfterModal();
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
                HidePreviewObjectsForModal();
                MessageBox.Show("타임라인에서 비디오 또는 오디오 클립을 선택해주세요.", "클립 선택 필요", MessageBoxButton.OK, MessageBoxImage.Warning);
                RestorePreviewObjectsAfterModal();
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
                HidePreviewObjectsForModal();
                MessageBox.Show("선택된 클립은 음성 텍스트 변환을 지원하지 않습니다.", "지원되지 않는 클립", MessageBoxButton.OK, MessageBoxImage.Warning);
                RestorePreviewObjectsAfterModal();
                return;
            }

            if (string.IsNullOrEmpty(mediaPath))
            {
                HidePreviewObjectsForModal();
                MessageBox.Show("선택된 클립의 미디어 경로를 찾을 수 없습니다.", "경로 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                RestorePreviewObjectsAfterModal();
                return;
            }

            // Pause playback before starting transcription UI
            if (IsTimelinePlaying)
            {
                _timelineTimer.Stop();
                PlayerViewModel.PauseAllPlayers();
                IsTimelinePlaying = false;
            }

            // Hide preview objects during script generation (transcription)
            HidePreviewObjectsForModal();

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

                // Restore preview objects after work completes
                RestorePreviewObjectsAfterModal();
            }
        }



        // Store saved preview state for restoration after export
        private List<TimelineClipBase>? _savedActiveVideoClips;
        private List<TimelineClipBase>? _savedActiveWpfOverlays;
        private bool _wasOverlayVisible;

        private async Task StartExportProcessAsync()
        {
            // Pause playback if currently playing before starting export
            if (IsTimelinePlaying)
            {
                _timelineTimer.Stop();
                PlayerViewModel.PauseAllPlayers();
                IsTimelinePlaying = false;
            }

            _exportCts = new CancellationTokenSource();

            if (_mainWindow == null)
            {
                HidePreviewObjectsForModal();
                MessageBox.Show("오류: 메인 윈도우를 찾을 수 없습니다.");
                RestorePreviewObjectsAfterModal();
                return;
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "MP4 Video (*.mp4)|*.mp4",
                Title = "편집된 영상 저장하기",
                FileName = "output.mp4"
            };

            // Hide preview objects while selecting path; keep hidden if proceeding to render
            HidePreviewObjectsForModal();
            bool? dialogResult = saveFileDialog.ShowDialog(_mainWindow);
            if (dialogResult != true)
            {
                // Restore only on cancel/close of dialog
                RestorePreviewObjectsAfterModal();
                _exportCts.Dispose();
                _exportCts = null;
                return;
            }

            // Proceed to render: keep preview hidden until user clicks '확인' on progress window
            IsExporting = true;
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
                    _exportCts.Token,
                    _referencePreviewWidth,
                    _referencePreviewHeight);

                if (success)
                {
                    progressViewModel.StatusMessage = $"성공! 영상이 '{saveFileDialog.FileName}'에 저장되었습니다. 미리보기는 '확인' 버튼을 누르면 복원됩니다.";
                    progressViewModel.IsFinished = true; // Do NOT restore here
                }
                else
                {
                    if (!_exportCts.Token.IsCancellationRequested)
                    {
                        progressViewModel.StatusMessage = $"오류: 렌더링에 실패했습니다.";
                    }
                    progressViewModel.IsFinished = true; // Still wait for user to close window
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

                // NOTE: 오버레이 객체 복원은 완료 버튼 클릭 시 수행됨 (RestorePreviewObjects 메서드)
            }
        }

        // Deprecated: use RestorePreviewObjectsAfterModal for robust rebuild
        public void RestorePreviewObjects()
        {
            RestorePreviewObjectsAfterModal();
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
                // Update reference position when user moves the clip
                if (sender is TimelineClipBase clip && clip.IsUserPositioned && 
                    _referencePreviewWidth > 1 && _referencePreviewHeight > 1)
                {
                    // Since preview is now fixed at 1920x1080, reference values are the same as actual values
                    clip.ReferenceX = clip.X;
                    clip.ReferenceY = clip.Y;
                }
                
                // X, Y 위치가 변경되면 비디오 clipping을 즉시 업데이트
                // 재생 중 드래그 시 비디오가 UI 위로 나타나는 것을 방지
                if (sender is VideoClip)
                {
                    // Force immediate clipping update without waiting for timer
                    VideoClipZOrderChanged?.Invoke(this, EventArgs.Empty);
                }
            }
            else if (e.PropertyName == nameof(TimelineClipBase.RenderWidth) || e.PropertyName == nameof(TimelineClipBase.RenderHeight))
            {
                // Update reference size when user resizes the clip
                if (sender is TimelineClipBase clip && clip.IsUserPositioned && 
                    _referencePreviewWidth > 1 && _referencePreviewHeight > 1)
                {
                    // Since preview is now fixed at 1920x1080, reference values are the same as actual values
                    clip.ReferenceRenderWidth = clip.RenderWidth;
                    clip.ReferenceRenderHeight = clip.RenderHeight;
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
            Debug.WriteLine($"[SeekTimeline] Setting CurrentTimelinePosition to: {timeSec}");
            if (IsTimelinePlaying)
            {
                _timelineTimer.Stop();
                PlayerViewModel.PauseAllPlayers();
                IsTimelinePlaying = false;
            }

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
                    if (!IsTimelinePlaying)
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
                    if (!IsTimelinePlaying)
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
            // Since VideoPlayerHost is now fixed at 1920x1080 inside a Viewbox,
            // we use these fixed dimensions as the reference
            const double fixedPreviewWidth = 1920.0;
            const double fixedPreviewHeight = 1080.0;
            
            // If this is the first initialization, store the reference preview size
            if (_referencePreviewWidth <= 1 || _referencePreviewHeight <= 1)
            {
                _referencePreviewWidth = fixedPreviewWidth;
                _referencePreviewHeight = fixedPreviewHeight;
                
                // Initialize all clips with this preview size
                foreach (var clip in VideoEditor.TimelineClips)
                {
                    if (clip is VideoClip videoClip)
                    {
                        InitializeVideoClipLayout(videoClip, fixedPreviewWidth, fixedPreviewHeight);
                    }
                    else if (clip is ImageClip imageClip)
                    {
                        InitializeImageClipLayout(imageClip, fixedPreviewWidth, fixedPreviewHeight);
                    }
                    else if (clip is TextClip textClip)
                    {
                        InitializeTextClipLayout(textClip, fixedPreviewWidth, fixedPreviewHeight);
                    }
                }
                return;
            }
            
            // Since preview size is now fixed, no scaling is needed
            // This method will only be called for the first initialization
        }

        private void InitializeVideoClipLayout(VideoClip videoClip, double previewWidth, double previewHeight)
        {
            if (videoClip.SourceWidth <= 0 || videoClip.SourceHeight <= 0) return;

            double videoAspectRatio = (double)videoClip.SourceWidth / videoClip.SourceHeight;
            double previewAspectRatio = previewWidth / previewHeight;

            double renderWidth, renderHeight, x, y;

            if (previewAspectRatio > videoAspectRatio)
            {
                renderHeight = previewHeight;
                renderWidth = renderHeight * videoAspectRatio;
            }
            else
            {
                renderWidth = previewWidth;
                renderHeight = renderWidth / videoAspectRatio;
            }

            x = (previewWidth - renderWidth) / 2;
            y = (previewHeight - renderHeight) / 2;

            // Store reference values
            videoClip.ReferenceX = x;
            videoClip.ReferenceY = y;
            videoClip.ReferenceRenderWidth = renderWidth;
            videoClip.ReferenceRenderHeight = renderHeight;
            
            // Apply values
            videoClip.X = x;
            videoClip.Y = y;
            videoClip.RenderWidth = renderWidth;
            videoClip.RenderHeight = renderHeight;
            
            videoClip.MarkInitialLayoutComplete();
        }

        private void UpdateVideoClipLayout(VideoClip videoClip, double previewWidth, double previewHeight, double widthRatio, double heightRatio)
        {
            if (videoClip.SourceWidth <= 0 || videoClip.SourceHeight <= 0) return;

            // If user has positioned the clip, scale from reference values
            if (videoClip.IsUserPositioned)
            {
                // Scale position and size from reference values
                videoClip.X = videoClip.ReferenceX * widthRatio;
                videoClip.Y = videoClip.ReferenceY * heightRatio;
                videoClip.RenderWidth = videoClip.ReferenceRenderWidth * widthRatio;
                videoClip.RenderHeight = videoClip.ReferenceRenderHeight * heightRatio;
                return;
            }

            // For clips not yet positioned by user, recalculate centered layout
            InitializeVideoClipLayout(videoClip, previewWidth, previewHeight);
        }

        private void InitializeImageClipLayout(ImageClip imageClip, double previewWidth, double previewHeight)
        {
            if (imageClip.SourceWidth <= 0 || imageClip.SourceHeight <= 0) return;

            double imageAspectRatio = (double)imageClip.SourceWidth / imageClip.SourceHeight;
            double previewAspectRatio = previewWidth / previewHeight;

            double renderWidth, renderHeight, x, y;

            if (previewAspectRatio > imageAspectRatio)
            {
                renderHeight = previewHeight;
                renderWidth = renderHeight * imageAspectRatio;
            }
            else
            {
                renderWidth = previewWidth;
                renderHeight = renderWidth / imageAspectRatio;
            }

            x = (previewWidth - renderWidth) / 2;
            y = (previewHeight - renderHeight) / 2;

            // Store reference values
            imageClip.ReferenceX = x;
            imageClip.ReferenceY = y;
            imageClip.ReferenceRenderWidth = renderWidth;
            imageClip.ReferenceRenderHeight = renderHeight;
            
            // Apply values
            imageClip.X = x;
            imageClip.Y = y;
            imageClip.RenderWidth = renderWidth;
            imageClip.RenderHeight = renderHeight;
            
            // Store initial render size for CustomWidth/Height calculations
            imageClip.InitialRenderWidth = renderWidth;
            imageClip.InitialRenderHeight = renderHeight;
            
            // Initialize CustomWidth/Height if not set
            if (imageClip.CustomWidth == 0 && imageClip.CustomHeight == 0)
            {
                imageClip.CustomWidth = imageClip.SourceWidth;
                imageClip.CustomHeight = imageClip.SourceHeight;
            }
            
            imageClip.MarkInitialLayoutComplete();
        }

        private void UpdateImageClipLayout(ImageClip imageClip, double previewWidth, double previewHeight, double widthRatio, double heightRatio)
        {
            if (imageClip.SourceWidth <= 0 || imageClip.SourceHeight <= 0) return;

            // If user has positioned the clip, scale from reference values
            if (imageClip.IsUserPositioned)
            {
                // Scale position and size from reference values
                imageClip.X = imageClip.ReferenceX * widthRatio;
                imageClip.Y = imageClip.ReferenceY * heightRatio;
                imageClip.RenderWidth = imageClip.ReferenceRenderWidth * widthRatio;
                imageClip.RenderHeight = imageClip.ReferenceRenderHeight * heightRatio;
                
                // Update InitialRenderWidth/Height to maintain custom size ratios
                imageClip.InitialRenderWidth = imageClip.ReferenceRenderWidth * widthRatio;
                imageClip.InitialRenderHeight = imageClip.ReferenceRenderHeight * heightRatio;
                return;
            }

            // For clips not yet positioned by user, recalculate centered layout
            InitializeImageClipLayout(imageClip, previewWidth, previewHeight);
        }

        private void InitializeTextClipLayout(TextClip textClip, double previewWidth, double previewHeight)
        {
            // Text clips: 화면 하단 중앙에 배치
            // RenderWidth/Height는 텍스트를 담을 영역의 크기
            double renderWidth = previewWidth * 0.6; // 60% of preview width
            double renderHeight = 150; // 고정 높이 (1080p 기준으로 적절한 크기)
            
            // X, Y는 좌상단 기준이므로 중앙 정렬을 위해 계산
            double x = (previewWidth - renderWidth) / 2; // 수평 중앙
            double y = previewHeight - renderHeight - 100; // 하단에서 100px 위

            // Store reference values
            textClip.ReferenceX = x;
            textClip.ReferenceY = y;
            textClip.ReferenceRenderWidth = renderWidth;
            textClip.ReferenceRenderHeight = renderHeight;
            
            // Apply values
            textClip.X = x;
            textClip.Y = y;
            textClip.RenderWidth = renderWidth;
            textClip.RenderHeight = renderHeight;
            
            textClip.MarkInitialLayoutComplete();
        }

        private void UpdateTextClipLayout(TextClip textClip, double previewWidth, double previewHeight, double widthRatio, double heightRatio)
        {
            // If user has positioned the clip, scale from reference values
            if (textClip.IsUserPositioned)
            {
                textClip.X = textClip.ReferenceX * widthRatio;
                textClip.Y = textClip.ReferenceY * heightRatio;
                textClip.RenderWidth = textClip.ReferenceRenderWidth * widthRatio;
                textClip.RenderHeight = textClip.ReferenceRenderHeight * heightRatio;
                return;
            }

            // For clips not yet positioned by user, recalculate default layout
            InitializeTextClipLayout(textClip, previewWidth, previewHeight);
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

        public void StartVideoClipPreviewDrag()
        {
            if (_isVideoClipBeingDraggedInPreview) return; // Already dragging

            _isVideoClipBeingDraggedInPreview = true;

            // Save current WPF overlays and hide them
            _wpfOverlaysHiddenDuringVideoDrag = new List<TimelineClipBase>(ActiveWpfOverlays);
            ActiveWpfOverlays.Clear();

            Debug.WriteLine("[VIDEO DRAG] Video clip drag started in preview - hiding WPF overlays");
        }

        public void EndVideoClipPreviewDrag()
        {
            if (!_isVideoClipBeingDraggedInPreview) return; // Not dragging

            _isVideoClipBeingDraggedInPreview = false;

            // Restore WPF overlays
            if (_wpfOverlaysHiddenDuringVideoDrag != null)
            {
                foreach (var clip in _wpfOverlaysHiddenDuringVideoDrag)
                {
                    if (!ActiveWpfOverlays.Contains(clip))
                    {
                        ActiveWpfOverlays.Add(clip);
                    }
                }
                _wpfOverlaysHiddenDuringVideoDrag = null;
            }

            Debug.WriteLine("[VIDEO DRAG] Video clip drag ended in preview - restoring WPF overlays");
        }

        public Window? GetMainWindow()
        {
            return _mainWindow;
        }
    }
}
