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
        //public event EventHandler? ExportFinished;
        private Window? _mainWindow;
        private TranscriptionProgressWindow? _transcriptionProgressWindow;
        private CancellationTokenSource? _exportCts;

        public ObservableCollection<TimelineClipBase> ActiveVisualClips { get; } = new();

        // VideoView visibility control (5 video players)
        private Visibility[] _videoViewVisibilities = new Visibility[5];
        public Visibility VideoView0Visibility { get => _videoViewVisibilities[0]; set => SetProperty(ref _videoViewVisibilities[0], value); }
        public Visibility VideoView1Visibility { get => _videoViewVisibilities[1]; set => SetProperty(ref _videoViewVisibilities[1], value); }
        public Visibility VideoView2Visibility { get => _videoViewVisibilities[2]; set => SetProperty(ref _videoViewVisibilities[2], value); }
        public Visibility VideoView3Visibility { get => _videoViewVisibilities[3]; set => SetProperty(ref _videoViewVisibilities[3], value); }
        public Visibility VideoView4Visibility { get => _videoViewVisibilities[4]; set => SetProperty(ref _videoViewVisibilities[4], value); }

        // VideoView ZIndex control based on active VideoClip TrackIndex
        private int[] _videoViewZIndices = new int[5];
        public int VideoView0ZIndex { get => _videoViewZIndices[0]; set => SetProperty(ref _videoViewZIndices[0], value); }
        public int VideoView1ZIndex { get => _videoViewZIndices[1]; set => SetProperty(ref _videoViewZIndices[1], value); }
        public int VideoView2ZIndex { get => _videoViewZIndices[2]; set => SetProperty(ref _videoViewZIndices[2], value); }
        public int VideoView3ZIndex { get => _videoViewZIndices[3]; set => SetProperty(ref _videoViewZIndices[3], value); }
        public int VideoView4ZIndex { get => _videoViewZIndices[4]; set => SetProperty(ref _videoViewZIndices[4], value); }




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
                if (!IsScrubbing)
                {
                    _wasPlayingBeforeInteraction = IsTimelinePlaying;
                    if (IsTimelinePlaying)
                    {
                        _timelineTimer.Stop();
                        PlayerViewModel.PauseAllPlayers();
                        IsTimelinePlaying = false;
                    }
                    _scrubSeekTimer.Start(); // Start the periodic seek timer
                }

                IsScrubbing = true;
                _scrubbingTimer.Stop(); // Reset the "end of scrub" timer
                _scrubbingTimer.Start();

                // Just update the position. The timer will do the seek.
                if (SetProperty(ref _currentTimelinePosition, value / 1000.0, nameof(CurrentTimelinePosition)))
                {
                    OnPropertyChanged(nameof(CurrentTimelineTimeMs));
                }
            }
        }

        private void ScrubbingTimer_Tick(object? sender, EventArgs e)
        {
            _scrubbingTimer.Stop();
            _scrubSeekTimer.Stop(); // Stop the periodic seeking
            IsScrubbing = false;

            if (_wasPlayingBeforeInteraction)
            {
                ResyncAndPlay();
                _wasPlayingBeforeInteraction = false;
            }
            else
            {
                // If we were paused before scrubbing, just do a final soft seek to land on the right frame.
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
            for (int i = 0; i < 5; i++)
            {
                _videoViewVisibilities[i] = Visibility.Visible;
                _videoViewZIndices[i] = 0;
            }
            
            _scrubbingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _scrubbingTimer.Tick += ScrubbingTimer_Tick;

            _ffmpegExportService = new FFmpegExportService();
            _projectService = new ProjectService();
            PlayerViewModel = new PlayerViewModel();
            VideoList = new VideoListViewModel();
            VideoEditor = new VideoEditorViewModel();
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
            VideoEditor.ClipInteractionStarted += StopPlayback;
            VideoEditor.ClipInteractionEnded += ResumePlaybackIfNeeded;

            PlayerViewModel.PropertyChanged += PlayerViewModel_PropertyChanged;

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
                    if (e.OldItems != null)
                    {
                        foreach (TimelineClipBase oldClip in e.OldItems)
                        {
                            oldClip.PropertyChanged -= Clip_PropertyChanged;
                        }
                    }
                    UpdateTotalTimelineDuration();
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
                Interval = TimeSpan.FromMilliseconds(50)
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

            CurrentTimelinePosition += _timelineTimer.Interval.TotalSeconds;

            // Check for end of timeline FIRST to prevent race conditions.
            if (CurrentTimelinePosition * 1000 >= TotalTimelineDurationMs)
            {
                ExecuteStopTimeline();
                return; // Stop processing this tick.
            }

            // If not the end, then sync players for the new position.
            SyncPlayersToTimeline();
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
                UpdateTotalTimelineDuration();
                    // StartPosition이 변경되고, 클립이 현재 활성 상태인 경우,
                    // 올바른 미디어 로딩/탐색을 보장하기 위해 플레이어를 다시 동기화해야함.
                if (sender is TimelineClipBase changedClip)
                {
                        // 변경된 클립이 현재 활성 상태인지 확인.
                    if (CurrentTimelinePosition >= changedClip.StartPosition &&
                        CurrentTimelinePosition < (changedClip.StartPosition + changedClip.Duration))
                    {
                                // player.Media가 새 StartPosition으로 재생성되도록 보장하기 위해 강제 재동기화
                        SyncPlayersToTimeline();
                    }
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
                    // 변경된 클립이 현재 활성 클립인지와 타임라인이 재생 중인지 확인
                    if (IsTimelinePlaying && CurrentTimelinePosition >= changedClip.StartPosition &&
                        CurrentTimelinePosition < (changedClip.StartPosition + changedClip.Duration))
                    {
                        MediaPlayer? player = null;
                        if (changedClip is VideoClip || changedClip is ImageClip)
                        {
                            _activeVisualClipPlayers.TryGetValue(changedClip, out player);
                        }
                        else if (changedClip is AudioClip)
                        {
                            _activeAudioPlayers.TryGetValue(changedClip, out player);
                        }

                        if (player != null)
                        {
                            double timeInOriginalMediaMs = player.Time; // original media 시간(ms)
                            double timeInOriginalMediaSec = timeInOriginalMediaMs / 1000.0;

                                        // 타임라인에서 클립 내의 새로운 시간을 계산
                            double newTimeWithinClipSec = timeInOriginalMediaSec / changedClip.SpeedRatio;

                            double newCurrentTimelinePosition = changedClip.StartPosition + newTimeWithinClipSec;

                                        // CurrentTimelinePosition이 새 클립 지속 시간을 초과하지 않도록 보장
                            double newClipEndTime = changedClip.StartPosition + changedClip.Duration;
                            if (newCurrentTimelinePosition > newClipEndTime)
                            {
                                newCurrentTimelinePosition = newClipEndTime;
                            }
                            if (newCurrentTimelinePosition < changedClip.StartPosition)
                            {
                                newCurrentTimelinePosition = changedClip.StartPosition;
                            }

                                        // 쓰로틀링: 너무 작은 변화는 무시
                            if (Math.Abs(CurrentTimelinePosition - newCurrentTimelinePosition) > 0.01)
                            {
                                CurrentTimelinePosition = newCurrentTimelinePosition;
                                SyncPlayersToTimeline(); // 조정된 위치로 플레이어를 재동기화
                            }
                        }
                    }

                    // 다른 클립에 영향을 줄 수 있거나 전체 길이가 변경될 수 있으므로 무조건 전체 지속 시간을 업데이트
                    UpdateTotalTimelineDuration();

                    if (_activeVisualClipPlayers.TryGetValue(changedClip, out var visualPlayer))
                    {
                        visualPlayer.SetRate((float)changedClip.SpeedRatio);
                    }
                    if (_activeAudioPlayers.TryGetValue(changedClip, out var audioPlayer))
                    {
                        audioPlayer.SetRate((float)changedClip.SpeedRatio);
                    }
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
                IsTimelinePlaying = true;
                _timelineTimer.Start();
                _wasPlayingBeforeInteraction = false; 
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
            bool hasClips = VideoEditor.TimelineClips.Any();
            PlayerViewModel.IsControlBarVisible = hasClips;
            PlayerViewModel.VideoViewBackground = hasClips ? new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black) : new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#525252"));

            var activeClips = VideoEditor.TimelineClips
                .Where(c => c.StartPosition <= CurrentTimelinePosition && (c.StartPosition + c.Duration) > CurrentTimelinePosition)
                .ToList();

            // Update ActiveVisualClips for the overlay
            // Only ImageClip and TextClip go to overlay (not VideoClip)
            // Update collection without clearing to avoid recreating adorners
            var activeVisualClipsForOverlay = activeClips
                .Where(c => c is ImageClip || c is TextClip)
                .OrderBy(c => c.TrackIndex)
                .ToList();

            // Remove clips that are no longer active
            for (int i = ActiveVisualClips.Count - 1; i >= 0; i--)
            {
                if (!activeVisualClipsForOverlay.Contains(ActiveVisualClips[i]))
                {
                    ActiveVisualClips.RemoveAt(i);
                }
            }

            // Add new active clips
            foreach (var clip in activeVisualClipsForOverlay)
            {
                if (!ActiveVisualClips.Contains(clip))
                {
                    // Find correct position to maintain order
                    int insertIndex = 0;
                    for (int i = 0; i < ActiveVisualClips.Count; i++)
                    {
                        if (ActiveVisualClips[i].TrackIndex > clip.TrackIndex)
                        {
                            break;
                        }
                        insertIndex = i + 1;
                    }
                    ActiveVisualClips.Insert(insertIndex, clip);
                }
            }

            // Update VideoView ZIndex based on active VideoClips' TrackIndex
            var activeVideoClipsForZIndex = activeClips.OfType<VideoClip>().ToList();
            UpdateVideoViewZIndices(activeVideoClipsForZIndex);
            
            // All VideoViews are visible (no hiding)
            UpdateVideoViewVisibilities(new HashSet<int>());

            // Handle video clips
            var activeVideoClips = activeClips.OfType<VideoClip>().ToList();
            var videoClipsToDeactivate = _activeVisualClipPlayers.Keys.OfType<VideoClip>().Except(activeVideoClips).ToList();
            
            foreach (var clip in videoClipsToDeactivate)
            {
                if (_activeVisualClipPlayers.Remove(clip, out var player))
                {
                    player.Stop();
                    player.Media?.Dispose();
                    player.Media = null;
                }
            }

            foreach (var videoClip in activeVideoClips)
            {
                var player = PlayerViewModel.VideoPlayers[videoClip.TrackIndex];
                double timeWithinClip = CurrentTimelinePosition - videoClip.StartPosition;

                if (!_activeVisualClipPlayers.ContainsKey(videoClip) || _activeVisualClipPlayers[videoClip] != player)
                {
                    _activeVisualClipPlayers[videoClip] = player;
                    string mediaPath = videoClip.VideoPath;
                    if (!string.IsNullOrEmpty(mediaPath))
                    {
                        player.Media = PlayerViewModel.PrepareMedia(mediaPath, timeWithinClip * videoClip.SpeedRatio, videoOnly: true, audioOnly: false);
                        player.SetRate((float)videoClip.SpeedRatio);
                    }
                }

                if (IsScrubbing || VideoEditor.IsDraggingClip)
                {
                    player.Time = (long)(timeWithinClip * videoClip.SpeedRatio * 1000);
                    player.Play();
                    player.SetPause(true);
                }
                else if (IsTimelinePlaying && !player.IsPlaying)
                {
                    player.Play();
                }
                else if (!IsTimelinePlaying && player.IsPlaying)
                {
                    player.SetPause(true);
                }
            }

            // Handle audio
            var activeAudioSourceClips = activeClips.Where(c => c is VideoClip || c is AudioClip).ToList();
            var audioClipsToDeactivate = _activeAudioPlayers.Keys.Except(activeAudioSourceClips).ToList();
            
            foreach (var clip in audioClipsToDeactivate)
            {
                if (_activeAudioPlayers.Remove(clip, out var player))
                {
                    player.Stop();
                    player.Media?.Dispose();
                    player.Media = null;
                }
            }

            foreach (var clip in activeAudioSourceClips)
            {
                MediaPlayer? player;
                double timeWithinClip = CurrentTimelinePosition - clip.StartPosition;
                double sourceStartTime = (clip is VideoClip vc) ? vc.SourceStartTime : (clip as AudioClip)?.SourceStartTime ?? 0;

                if (!_activeAudioPlayers.TryGetValue(clip, out player))
                {
                    player = PlayerViewModel.GetAvailableAudioPlayer();
                    if (player == null) { continue; }
                    _activeAudioPlayers.Add(clip, player);

                    string mediaPath = (clip is VideoClip v) ? v.VideoPath : (clip as AudioClip)?.AudioPath ?? string.Empty;
                    if (!string.IsNullOrEmpty(mediaPath))
                    {
                        using var equalizer = new Equalizer(_flatEqIndex);
                        int combinedVolume = (int)((clip.Volume / 100.0) * (PlayerViewModel.Volume / 100.0) * 100);
                        equalizer.SetPreamp(ConvertVolumeToDb(combinedVolume));
                        player.SetEqualizer(equalizer);

                        player.Media = PlayerViewModel.PrepareMedia(mediaPath, sourceStartTime + (timeWithinClip * clip.SpeedRatio), videoOnly: false, audioOnly: true);
                        player.SetRate((float)clip.SpeedRatio);
                    }
                }

                if (player != null)
                {
                    if (IsScrubbing || VideoEditor.IsDraggingClip)
                    {
                        player.Time = (long)((sourceStartTime + (timeWithinClip * clip.SpeedRatio)) * 1000);
                        player.Play();
                        player.SetPause(true);
                    }
                    else if (IsTimelinePlaying && !player.IsPlaying)
                    {
                        player.Time = (long)((sourceStartTime + (timeWithinClip * clip.SpeedRatio)) * 1000);
                        player.Play();
                    }
                    else if (!IsTimelinePlaying && player.IsPlaying)
                    {
                        player.SetPause(true);
                    }
                }
            }
        }

        private void UpdateVideoViewVisibilities(HashSet<int> videoClipsInOverlay)
        {
            // Update visibility for each VideoView (0-4)
            // Hide VideoView if there's a VideoClip in overlay at that track index
            VideoView0Visibility = videoClipsInOverlay.Contains(0) ? Visibility.Collapsed : Visibility.Visible;
            VideoView1Visibility = videoClipsInOverlay.Contains(1) ? Visibility.Collapsed : Visibility.Visible;
            VideoView2Visibility = videoClipsInOverlay.Contains(2) ? Visibility.Collapsed : Visibility.Visible;
            VideoView3Visibility = videoClipsInOverlay.Contains(3) ? Visibility.Collapsed : Visibility.Visible;
            VideoView4Visibility = videoClipsInOverlay.Contains(4) ? Visibility.Collapsed : Visibility.Visible;
            
            OnPropertyChanged(nameof(VideoView0Visibility));
            OnPropertyChanged(nameof(VideoView1Visibility));
            OnPropertyChanged(nameof(VideoView2Visibility));
            OnPropertyChanged(nameof(VideoView3Visibility));
            OnPropertyChanged(nameof(VideoView4Visibility));
        }

        private void UpdateVideoViewZIndices(List<VideoClip> activeVideoClips)
        {
            // Reset all ZIndices to 0
            for (int i = 0; i < 5; i++)
            {
                _videoViewZIndices[i] = 0;
            }

            // Set ZIndex based on TrackIndex of active VideoClips
            // Higher TrackIndex = Higher ZIndex (appears on top)
            foreach (var clip in activeVideoClips)
            {
                if (clip.TrackIndex >= 0 && clip.TrackIndex < 5)
                {
                    _videoViewZIndices[clip.TrackIndex] = clip.TrackIndex;
                }
            }

            OnPropertyChanged(nameof(VideoView0ZIndex));
            OnPropertyChanged(nameof(VideoView1ZIndex));
            OnPropertyChanged(nameof(VideoView2ZIndex));
            OnPropertyChanged(nameof(VideoView3ZIndex));
            OnPropertyChanged(nameof(VideoView4ZIndex));
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


        public void Dispose()
        {
            PlayerViewModel?.Dispose();
            VideoEditor?.Dispose();

            if (VideoEditor != null)
            {
                VideoEditor.OnClipAdded -= MainViewModel_OnClipAdded;
            }
        }
    }
}
