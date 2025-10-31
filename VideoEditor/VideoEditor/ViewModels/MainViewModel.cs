using System.Collections.ObjectModel;
using System.Windows;
using System.Diagnostics;
using Microsoft.Win32;
using System.Text;
using CommunityToolkit.Mvvm.Input;
using VideoEditor.Common;
using VideoEditor.Models;
using System.Windows.Threading;
using System.Globalization;
using System.IO;
using LibVLCSharp.Shared;
using System.Threading;

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
        public PlayerViewModel PlayerViewModel { get; }
        public VideoListViewModel VideoList { get; }
        public VideoEditorViewModel VideoEditor { get; }
        public EditorHostViewModel EditorHost { get; }
        public string StatusMessage { get; set; } = "준비 완료";
        public IAsyncRelayCommand ExportVideoCommand { get; }
        public IAsyncRelayCommand TranscribeVideoCommand { get; }

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
        public event EventHandler? ExportFinished;
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
            // Initialize VideoView visibilities to Visible
            for (int i = 0; i < 5; i++)
            {
                _videoViewVisibilities[i] = Visibility.Visible;
                _videoViewZIndices[i] = 0; // Default ZIndex
            }
            
            _scrubbingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(200) };
            _scrubbingTimer.Tick += ScrubbingTimer_Tick;

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

            _timelineTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _timelineTimer.Tick += OnTimelineTimerTick;

            _scrubSeekTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _scrubSeekTimer.Tick += ScrubSeekTimer_Tick;

            UpdateTotalTimelineDuration();
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
            Debug.WriteLine("[DEBUG] StartExportProcessAsync method has started.");
            _exportCts = new CancellationTokenSource();

            if (_mainWindow == null)
            {
                Debug.WriteLine("[DEBUG] CRITICAL: _mainWindow is NULL. The dialog cannot be shown correctly.");
                return;
            }
            else
            {
                Debug.WriteLine($"[DEBUG] _mainWindow is valid. Title: {_mainWindow.Title}");
            }

            var saveFileDialog = new SaveFileDialog
            {
                Filter = "MP4 Video (*.mp4)|*.mp4",
                Title = "편집된 영상 저장하기",
                FileName = "output.mp4"
            };

            Debug.WriteLine("[DEBUG] Showing SaveFileDialog now...");
            bool? dialogResult = saveFileDialog.ShowDialog(_mainWindow);
            Debug.WriteLine($"[DEBUG] SaveFileDialog returned with result: {dialogResult?.ToString() ?? "null"}");

            if (dialogResult != true)
            {
                Debug.WriteLine("[DEBUG] Dialog result was not 'true'. Aborting export process.");
                _exportCts.Dispose();
                _exportCts = null;
                return;
            }

            string outputPath = saveFileDialog.FileName;


            var progressViewModel = new ExportProgressViewModel(() => _exportCts.Cancel());
            ExportStarted?.Invoke(this, new ExportStartedEventArgs(progressViewModel));

            try
            {
                await RunExportLogicAsync(outputPath, progressViewModel, _exportCts.Token);
            }
            finally
            {
                _exportCts.Dispose();
                _exportCts = null;
            }
        }

        private async Task RunExportLogicAsync(string outputPath, ExportProgressViewModel progressViewModel, CancellationToken cancellationToken)
        {
            if (!VideoEditor.TimelineClips.Any())
            {
                Debug.WriteLine("[EXPORT] Error: No clips on the timeline.");
                StatusMessage = "내보낼 클립이 타임라인에 없습니다.";
                OnPropertyChanged(nameof(StatusMessage));
                return;
            }

            Debug.WriteLine($"[EXPORT] User's desired output path: {outputPath}");

            string tempWorkingDirectory = Path.Combine(Path.GetTempPath(), "VideoEditorExport", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempWorkingDirectory);
            Debug.WriteLine($"[EXPORT] Created temporary working directory: {tempWorkingDirectory}");

            string? tempScriptPath = null;
            bool exportSucceeded = false;

            try
            {
                var argumentsBuilder = new StringBuilder();
                var safePathMappings = new Dictionary<string, string>();
                var safeInputFiles = new List<string>();

                var uniqueSourceFiles = VideoEditor.TimelineClips
                    .Select(c => c switch
                    {
                        VideoClip vc => vc.VideoPath,
                        AudioClip ac => ac.AudioPath,
                        ImageClip ic => ic.ImagePath,
                        _ => null
                    })
                    .Where(path => !string.IsNullOrEmpty(path))
                    .Distinct()
                    .ToList();

                for (int i = 0; i < uniqueSourceFiles.Count; i++)
                {
                    string originalPath = uniqueSourceFiles[i];
                    string safeExtension = Path.GetExtension(originalPath);
                    string safeFileName = $"input_{i}{safeExtension}";
                    string safeTempPath = Path.Combine(tempWorkingDirectory, safeFileName);

                    Debug.WriteLine($"[EXPORT] Copying '{originalPath}' to '{safeTempPath}'");
                    await Task.Run(() => File.Copy(originalPath, safeTempPath, true));

                    safePathMappings.Add(originalPath, safeTempPath);
                    safeInputFiles.Add(safeTempPath);
                    argumentsBuilder.Append($"-i \"{safeTempPath}\" ");
                }

                var filterComplexBuilder = new StringBuilder();
                var orderedClips = VideoEditor.TimelineClips.OrderBy(c => c.StartPosition).ToList();
                double totalDurationSec = TotalTimelineDurationMs / 1000.0;

                string outputResolution = "1920x1080";
                string outputFrameRate = "30";
                string audioSampleRate = "44100";

                filterComplexBuilder.Append($"color=c=black:s={outputResolution}:r={outputFrameRate}:d={totalDurationSec.ToString("F6", CultureInfo.InvariantCulture)}[base_v];");
                filterComplexBuilder.Append($"anullsrc=r={audioSampleRate}:cl=stereo:d={totalDurationSec.ToString("F6", CultureInfo.InvariantCulture)}[base_a];");

                var videoStreamNamesToOverlay = new List<string>();
                var audioStreamNamesToMix = new List<string> { "[base_a]" };

                for (int i = 0; i < orderedClips.Count; i++)
                {
                    var clip = orderedClips[i];
                    switch (clip)
                    {
                        case VideoClip vc:
                            {
                                string safeClipPath = safePathMappings[vc.VideoPath];
                                int fileIndex = safeInputFiles.IndexOf(safeClipPath);
                                string sourceStartTime = vc.SourceStartTime.ToString("F6", CultureInfo.InvariantCulture);
                                string duration = vc.Duration.ToString("F6", CultureInfo.InvariantCulture);
                                string videoDelayTime = vc.StartPosition.ToString("F6", CultureInfo.InvariantCulture);
                                var audioDelayTimeMs = (long)(vc.StartPosition * 1000);

                                // 비디오 스트림 처리
                                string videoTrimmed = $"[v_trimmed{i}]";
                                string videoDelayed = $"[v_delayed{i}]";
                                filterComplexBuilder.Append($"[{fileIndex}:v]trim=start={sourceStartTime}:duration={duration},setpts=PTS-STARTPTS,scale={outputResolution},setsar=1{videoTrimmed};");
                                filterComplexBuilder.Append($"{videoTrimmed}setpts=PTS+{videoDelayTime}/TB{videoDelayed};");
                                videoStreamNamesToOverlay.Add(videoDelayed);

                                // 오디오 스트림 처리
                                filterComplexBuilder.Append($"[{fileIndex}:a]atrim=start={sourceStartTime}:duration={duration},asetpts=PTS-STARTPTS[a_trimmed{i}];");
                                filterComplexBuilder.Append($"[a_trimmed{i}]adelay={audioDelayTimeMs}|{audioDelayTimeMs}[a{i}];");
                                audioStreamNamesToMix.Add($"[a{i}]");
                                break;
                            }
                        case AudioClip ac:
                            {
                                string safeClipPath = safePathMappings[ac.AudioPath];
                                int fileIndex = safeInputFiles.IndexOf(safeClipPath);

                                string sourceStartTime = ac.SourceStartTime.ToString("F6", CultureInfo.InvariantCulture);
                                string duration = ac.Duration.ToString("F6", CultureInfo.InvariantCulture);
                                var audioDelayTimeMs = (long)(ac.StartPosition * 1000);

                                string audioTrimmed = $"[a_trimmed_ac{i}]"; // 변수명 충돌 방지
                                filterComplexBuilder.Append($"[{fileIndex}:a]atrim=start={sourceStartTime}:duration={duration},asetpts=PTS-STARTPTS{audioTrimmed};");

                                string audioDelayed = $"[a_ac{i}]";
                                filterComplexBuilder.Append($"{audioTrimmed}adelay={audioDelayTimeMs}|{audioDelayTimeMs}{audioDelayed};");

                                audioStreamNamesToMix.Add(audioDelayed);
                                break;
                            }
                        case ImageClip ic:
                            {
                                // (향후 ImageClip 처리 로직 추가)
                                break;
                            }
                    }
                }

                if (videoStreamNamesToOverlay.Any())
                {
                    string lastVideoOutput = "[base_v]";
                    for (int i = 0; i < videoStreamNamesToOverlay.Count; i++)
                    {
                        string streamToOverlay = videoStreamNamesToOverlay[i];
                        string newVideoOutput = (i == videoStreamNamesToOverlay.Count - 1) ? "[out_v]" : $"[v_merged{i}]";

                        filterComplexBuilder.Append($"{lastVideoOutput}{streamToOverlay}overlay=x=0:y=0:eof_action=pass{newVideoOutput};");
                        lastVideoOutput = newVideoOutput;
                    }
                }
                else
                {
                    filterComplexBuilder.Append("[base_v]null[out_v];");
                }

                string amixInputs = string.Join("", audioStreamNamesToMix);
                filterComplexBuilder.Append($"{amixInputs}amix=inputs={audioStreamNamesToMix.Count}[out_a]");

                tempScriptPath = Path.Combine(tempWorkingDirectory, "script.txt");
                await File.WriteAllTextAsync(tempScriptPath, filterComplexBuilder.ToString());

                string safeOutputPath = Path.Combine(tempWorkingDirectory, "output.mp4");

                argumentsBuilder.Append($"-filter_complex_script \"{tempScriptPath}\" ");
                argumentsBuilder.Append($"-map \"[out_v]\" -map \"[out_a]\" ");
                argumentsBuilder.Append($"-c:v libx264 -preset medium -crf 23 -c:a aac -b:a 192k -y \"{safeOutputPath}\" ");
                string arguments = argumentsBuilder.ToString();

                Debug.WriteLine("---- DEBUG START ----");
                Debug.WriteLine("Generated Temp Script Path:");
                Debug.WriteLine(tempScriptPath);
                Debug.WriteLine("\nTemp Script Content:");
                Debug.WriteLine(await File.ReadAllTextAsync(tempScriptPath));
                Debug.WriteLine("\nFinal FFmpeg Command:");
                Debug.WriteLine($"ffmpeg.exe {arguments}");
                Debug.WriteLine("---- DEBUG END ----");

                var processStartInfo = new ProcessStartInfo
                {
                    FileName = @"ffmpeg\bin\ffmpeg.exe",
                    Arguments = arguments,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = new Process { StartInfo = processStartInfo })
                {
                    process.OutputDataReceived += (sender, args) =>
                    {
                        if (!string.IsNullOrWhiteSpace(args.Data))
                            Debug.WriteLine($"[FFMPEG STDOUT]: {args.Data}");
                    };

                    process.ErrorDataReceived += (sender, args) =>
                    {
                        if (string.IsNullOrWhiteSpace(args.Data)) return;
                        Debug.WriteLine($"[FFMPEG LOG]: {args.Data}");

                        if (args.Data.Contains("time="))
                        {
                            try
                            {
                                string timeString = args.Data.Split("time=")[1].Split(" ")[0];

                                if (TimeSpan.TryParse(timeString, out TimeSpan currentTime))
                                {
                                    double progress = (currentTime.TotalSeconds / totalDurationSec) * 100;

                                    UIDispatcher.Invoke(() =>
                                    {
                                        progressViewModel.Progress = Math.Min(100, progress);
                                        progressViewModel.StatusMessage = $"렌더링 중... {progress:F1}%";
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"[PROGRESS PARSE ERROR] {ex.Message}");
                            }
                        }
                    };

                    Debug.WriteLine("[EXPORT] Starting FFmpeg process...");
                    process.Start();
                    process.BeginErrorReadLine();
                    process.BeginOutputReadLine();

                    await process.WaitForExitAsync(cancellationToken);

                    if (process.ExitCode == 0)
                    {
                        Debug.WriteLine("[EXPORT] FFmpeg processing successful. Moving file to final destination.");
                        File.Move(safeOutputPath, outputPath, true);
                        StatusMessage = $"성공! 영상이 '{outputPath}'에 저장되었습니다.";
                        OnPropertyChanged(nameof(StatusMessage));
                        exportSucceeded = true;
                    }
                    else
                    {
                        Debug.WriteLine($"[EXPORT] FFmpeg process failed with exit code: {process.ExitCode}.");
                        StatusMessage = $"오류: 렌더링에 실패했습니다. (종료 코드: {process.ExitCode})";
                        OnPropertyChanged(nameof(StatusMessage));
                    }
                }
            }
            catch (Exception ex)
            {
                StatusMessage = $"치명적인 오류 발생: {ex.Message}";
                OnPropertyChanged(nameof(StatusMessage));
                Debug.WriteLine($"[EXPORT] A critical error occurred: {ex.ToString()}");
            }
            finally
            {
                if (exportSucceeded && Directory.Exists(tempWorkingDirectory))
                {
                    try
                    {
                        Directory.Delete(tempWorkingDirectory, true);
                        Debug.WriteLine("[EXPORT] Temporary working directory cleaned up.");
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"[EXPORT] Failed to clean up temp directory: {ex.Message}");
                    }
                }
                else if (!exportSucceeded)
                {
                    Debug.WriteLine($"[EXPORT] Export failed. Temporary files are preserved for inspection at: {tempWorkingDirectory}");
                }

                ExportFinished?.Invoke(this, EventArgs.Empty);
                Debug.WriteLine("[EXPORT] Export process finished.");

                if (!exportSucceeded)
                {
                    // UI 스레드에서 MessageBox를 띄웁니다.
                    UIDispatcher.Invoke(() =>
                    {
                        MessageBox.Show(
                            _mainWindow, // 부모 창을 지정하여 중앙에 표시
                            "영상 내보내기에 실패했습니다.\n타임라인의 클립이나 파일 경로에 문제가 없는지 확인해주세요.",
                            "렌더링 오류",
                            MessageBoxButton.OK,
                            MessageBoxImage.Error);
                    });
                }
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
