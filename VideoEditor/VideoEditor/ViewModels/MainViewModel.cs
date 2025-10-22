using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using VideoEditor.Common;
using VideoEditor.Models;
using VlcMediaPlayer = LibVLCSharp.Shared.MediaPlayer;
using System.ComponentModel;

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

        private string _activeDisplayText = string.Empty;
        private bool _isTextVisible = false;

        private double _lastPlayerWidth;
        private double _lastPlayerHeight;

        private TimelineClipBase? _highlightedVisualClip;
        public TimelineClipBase? HighlightedVisualClip
        {
            get => _highlightedVisualClip;
            private set => SetProperty(ref _highlightedVisualClip, value);
        }

        private Thickness _highlightBorderMargin;
        public Thickness HighlightBorderMargin
        {
            get => _highlightBorderMargin;
            private set => SetProperty(ref _highlightBorderMargin, value);
        }

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

        private readonly Dictionary<TimelineClipBase, VlcMediaPlayer> _activeVisualClipPlayers = new();
        private readonly Dictionary<TimelineClipBase, (VlcMediaPlayer Player, Equalizer Eq)> _activeAudioPlayers = new();

        private readonly DispatcherTimer _timelineTimer;
        private readonly uint _flatEqIndex;

        public string ActiveDisplayText
        {
            get => _activeDisplayText;
            set => SetProperty(ref _activeDisplayText, value);
        }

        public bool IsTextVisible
        {
            get => _isTextVisible;
            set => SetProperty(ref _isTextVisible, value);
        }

        public bool IsScrubbing { get; set; }

        public MainViewModel()
        {
            PlayerViewModel = new PlayerViewModel();
            VideoList = new VideoListViewModel();
            VideoEditor = new VideoEditorViewModel();
            EditorHost = new EditorHostViewModel(PlayerViewModel, VideoEditor);

            VideoEditor.OnTimelineChanged += UpdateTotalTimelineDuration;


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

            VideoEditor.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(VideoEditor.SelectedClip))
                {
                    UpdateHighlightedVisualClip();
                }
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

        public void UpdateHighlightBorderSize(double playerWidth, double playerHeight)
        {
            _lastPlayerWidth = playerWidth;
            _lastPlayerHeight = playerHeight;

            foreach (var clip in VideoEditor.TimelineClips.OfType<VisualClipBase>())
            {
                clip.UpdateRenderContext(playerWidth, playerHeight);
            }

            if (HighlightedVisualClip == null || playerWidth <= 0 || playerHeight <= 0)
            {
                HighlightBorderMargin = new Thickness(0);
                return;
            }

            int contentWidth = 0;
            int contentHeight = 0;

            if (HighlightedVisualClip is VideoClip vc)
            {
                contentWidth = vc.SourceWidth;
                contentHeight = vc.SourceHeight;
            }
            else if (HighlightedVisualClip is ImageClip ic)
            {
                contentWidth = ic.SourceWidth;
                contentHeight = ic.SourceHeight;
            }

            if (contentWidth <= 0 || contentHeight <= 0)
            {
                HighlightBorderMargin = new Thickness(0);
                return;
            }

            double playerAspectRatio = playerWidth / playerHeight;
            double contentAspectRatio = (double)contentWidth / contentHeight;

            double marginH = 0;
            double marginV = 0;

            if (contentAspectRatio > playerAspectRatio)
            {
                double scaledHeight = playerWidth / contentAspectRatio;
                marginV = (playerHeight - scaledHeight) / 2.0;
            }
            else
            {
                double scaledWidth = playerHeight * contentAspectRatio;
                marginH = (playerWidth - scaledWidth) / 2.0;
            }

            HighlightBorderMargin = new Thickness(marginH, marginV, marginH, marginV);
        }

        private void UpdateHighlightedVisualClip()
        {
            var selected = VideoEditor.SelectedClip;

            if (selected == null || !(selected is VideoClip || selected is ImageClip || selected is TextClip))
            {
                HighlightedVisualClip = null;
            }
            else
            {
                bool isClipActive = CurrentTimelinePosition >= selected.StartPosition &&
                                    CurrentTimelinePosition < (selected.StartPosition + selected.Duration);

                HighlightedVisualClip = isClipActive ? selected : null;
            }
            UpdateHighlightBorderSize(_lastPlayerWidth, _lastPlayerHeight);
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

            SyncPlayersToTimeline();

            if (CurrentTimelinePosition * 1000 >= TotalTimelineDurationMs)
            {
                ExecuteStopTimeline();
            }
        }

        private void Clip_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (VideoEditor.IsDraggingClip)
            {
                return;
            }

            switch (e.PropertyName)
            {
                case nameof(VisualClipBase.PositionX):
                case nameof(VisualClipBase.PositionY):
                case nameof(VisualClipBase.Scale):
                    if (!IsTimelinePlaying)
                    {
                        SyncPlayersToTimeline();
                    }
                    break;

                case nameof(TimelineClipBase.StartPosition):
                    if (sender is TimelineClipBase clipStarted)
                    {
                        if (CurrentTimelinePosition >= clipStarted.StartPosition &&
                            CurrentTimelinePosition < (clipStarted.StartPosition + clipStarted.Duration))
                        {
                            SyncPlayersToTimeline();
                        }
                    }
                    break;

                case nameof(TimelineClipBase.Duration):
                    UpdateTotalTimelineDuration();
                    if (sender is TimelineClipBase changedClip)
                    {
                        if (CurrentTimelinePosition >= changedClip.StartPosition &&
                            CurrentTimelinePosition < (changedClip.StartPosition + changedClip.Duration))
                        {
                            SyncPlayersToTimeline();
                        }
                    }
                    break;

                case nameof(TimelineClipBase.Volume):
                    if (sender is TimelineClipBase changedClipWithVolume && (changedClipWithVolume is VideoClip || changedClipWithVolume is AudioClip))
                    {
                        if (_activeAudioPlayers.TryGetValue(changedClipWithVolume, out var playerAndEq))
                        {
                            int combinedVolume = (int)((changedClipWithVolume.Volume / 100.0) * (PlayerViewModel.Volume / 100.0) * 100);
                            var preampDb = ConvertVolumeToDb(combinedVolume);

                            playerAndEq.Eq.SetPreamp(preampDb);
                        }
                    }
                    break;

                case nameof(TimelineClipBase.SpeedRatio):
                    if (sender is TimelineClipBase changedClipWithSpeed)
                    {
                        if (IsTimelinePlaying && CurrentTimelinePosition >= changedClipWithSpeed.StartPosition &&
                            CurrentTimelinePosition < (changedClipWithSpeed.StartPosition + changedClipWithSpeed.Duration))
                        {
                            VlcMediaPlayer? player = null;
                            if (changedClipWithSpeed is VideoClip || changedClipWithSpeed is ImageClip)
                            {
                                _activeVisualClipPlayers.TryGetValue(changedClipWithSpeed, out player);
                            }
                            else if (changedClipWithSpeed is AudioClip)
                            {
                                _activeAudioPlayers.TryGetValue(changedClipWithSpeed, out var playerAndEq);
                            }

                            if (player != null)
                            {
                                double timeInOriginalMediaMs = player.Time; // original media 시간(ms)
                                double timeInOriginalMediaSec = timeInOriginalMediaMs / 1000.0;

                                // 타임라인에서 클립 내의 새로운 시간을 계산
                                double newTimeWithinClipSec = timeInOriginalMediaSec / changedClipWithSpeed.SpeedRatio;

                                double newCurrentTimelinePosition = changedClipWithSpeed.StartPosition + newTimeWithinClipSec;

                                // CurrentTimelinePosition이 새 클립 지속 시간을 초과하지 않도록 보장
                                double newClipEndTime = changedClipWithSpeed.StartPosition + changedClipWithSpeed.Duration;
                                if (newCurrentTimelinePosition > newClipEndTime)
                                {
                                    newCurrentTimelinePosition = newClipEndTime;
                                }
                                if (newCurrentTimelinePosition < changedClipWithSpeed.StartPosition)
                                {
                                    newCurrentTimelinePosition = changedClipWithSpeed.StartPosition;
                                }

                                // 쓰로틀링: 너무 작은 변화는 무시
                                if (Math.Abs(CurrentTimelinePosition - newCurrentTimelinePosition) > 0.01)
                                {
                                    CurrentTimelinePosition = newCurrentTimelinePosition;
                                    SyncPlayersToTimeline(); // 조정된 위치로 플레이어를 재동기화
                                }
                            }
                        }

                        UpdateTotalTimelineDuration();

                        if (_activeVisualClipPlayers.TryGetValue(changedClipWithSpeed, out var visualPlayer))
                        {
                            visualPlayer.SetRate((float)changedClipWithSpeed.SpeedRatio);
                        }
                        if (_activeAudioPlayers.TryGetValue(changedClipWithSpeed, out var audioPlayerAndEq))
                        {
                            audioPlayerAndEq.Player.SetRate((float)changedClipWithSpeed.SpeedRatio);
                        }
                    }
                    break;
            }
        }

        private void PlayerViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayerViewModel.Volume))
            {
                foreach (var (clip, playerAndEq) in _activeAudioPlayers)
                {
                    int combinedVolume = (int)((clip.Volume / 100.0) * (PlayerViewModel.Volume / 100.0) * 100);
                    var preampDb = ConvertVolumeToDb(combinedVolume);

                    playerAndEq.Eq.SetPreamp(preampDb);
                }
            }
        }

        private void MainViewModel_OnClipAdded(object? sender, ClipAddedEventArgs e)
        {
            var addedClip = VideoEditor.TimelineClips.LastOrDefault();
            if (addedClip is VisualClipBase visualClip && _lastPlayerWidth > 0 && _lastPlayerHeight > 0)
            {
                visualClip.UpdateRenderContext(_lastPlayerWidth, _lastPlayerHeight);
            }

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
                IsTimelinePlaying = true; 
                SeekTimeline(CurrentTimelinePosition); 
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
            Debug.WriteLine($"[SEEK] 타임라인 {timeSec:F2}초로 이동. Scrubbing: {isScrubbing}");

            bool wasPlaying = IsTimelinePlaying;

            // 스크러빙 중이 아닐 때만 플레이어를 완전히 멈추고 재설정.
            if (!isScrubbing)
            {
                if (wasPlaying)
                {
                    _timelineTimer.Stop();
                    IsTimelinePlaying = false; 
                }

                PlayerViewModel.Stop();
                _activeVisualClipPlayers.Clear();
                _activeAudioPlayers.Clear();
            }

            CurrentTimelinePosition = timeSec;

            if (wasPlaying && !isScrubbing)
            {
                IsTimelinePlaying = true; // 동기화 전에 true로 되돌리기
            }

            SyncPlayersToTimeline();

            if (wasPlaying && !isScrubbing)
            {
                _timelineTimer.Start();
            }
        }

        public void SyncPlayersToTimeline()
        {
            bool hasClips = VideoEditor.TimelineClips.Any();
            PlayerViewModel.IsControlBarVisible = hasClips;
            PlayerViewModel.VideoViewBackground = hasClips ? new SolidColorBrush(Colors.Black) : new SolidColorBrush((Color)ColorConverter.ConvertFromString("#525252"));

            var activeClips = VideoEditor.TimelineClips
                .Where(c => c.StartPosition <= CurrentTimelinePosition && (c.StartPosition + c.Duration) > CurrentTimelinePosition)
                .ToList();

            // ==================================================================
            // 1. 시각적 요소 처리 (VideoClips, ImageClips)
            // ==================================================================
            var activeVisualClips = activeClips.OfType<VisualClipBase>().ToList();
            var activeVisualPlayers = new HashSet<VlcMediaPlayer>();

            // 비활성화될 플레이어의 Transform을 먼저 초기화합니다.
            for (int i = 0; i < PlayerViewModel.VideoPlayers.Count; i++)
            {
                // 이번 프레임에서 활성화될 클립이 해당 트랙을 사용하는지 확인
                bool isTrackActive = activeVisualClips.Any(c => c.TrackIndex == i);
                if (!isTrackActive)
                {
                    var player = PlayerViewModel.VideoPlayers[i];
                    if (player.Media != null)
                    {
                        player.Stop();
                        player.Media.Dispose();
                        player.Media = null;
                    }
                    // [핵심] 사용하지 않는 트랙의 Transform을 초기화하여 이전 상태가 남지 않도록 합니다.
                    PlayerViewModel.VideoPlayerStates[i].Transform = Transform.Identity;
                }
            }
            _activeVisualClipPlayers.Keys.Except(activeVisualClips).ToList().ForEach(c => _activeVisualClipPlayers.Remove(c));

            // 활성화된 시각적 클립들을 처리합니다.
            foreach (var clip in activeVisualClips)
            {
                if (clip.TrackIndex >= PlayerViewModel.VideoPlayers.Count) continue;

                var player = PlayerViewModel.VideoPlayers[clip.TrackIndex];
                var playerState = PlayerViewModel.VideoPlayerStates[clip.TrackIndex];
                double timeWithinClip = CurrentTimelinePosition - clip.StartPosition;

                // [핵심 수정 2] 위치/크기 조절 Transform을 여기서 적용합니다.
                var transformGroup = new TransformGroup();
                transformGroup.Children.Add(new ScaleTransform(clip.Scale, clip.Scale));
                transformGroup.Children.Add(new TranslateTransform(clip.PositionX, clip.PositionY));
                transformGroup.Freeze();
                playerState.Transform = transformGroup;

                // 미디어 재생 로직
                if (!_activeVisualClipPlayers.ContainsKey(clip))
                {
                    _activeVisualClipPlayers[clip] = player;
                    string mediaPath = (clip is VideoClip vc) ? vc.VideoPath : ((ImageClip)clip).ImagePath;

                    // [핵심 수정 2] 영상 재생 시에도 SourceStartTime을 반영합니다.
                    double sourceStartTime = (clip is VideoClip v) ? v.SourceStartTime : 0;
                    double seekTime = sourceStartTime + (timeWithinClip * clip.SpeedRatio);

                    player.Media = PlayerViewModel.PrepareMedia(mediaPath, seekTime, videoOnly: true, audioOnly: false);
                    player.SetRate((float)clip.SpeedRatio);
                }

                // 재생/탐색 로직
                if (IsScrubbing || VideoEditor.IsDraggingClip)
                {
                    // [핵심 수정 2] 탐색 시에도 SourceStartTime을 반영합니다.
                    double sourceStartTime = (clip is VideoClip v) ? v.SourceStartTime : 0;
                    player.Time = (long)((sourceStartTime + (timeWithinClip * clip.SpeedRatio)) * 1000);
                    if (!player.IsPlaying) player.Play();
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

            // ==================================================================
            // 2. 청각적 요소 처리 (VideoClips의 오디오, AudioClips)
            // ==================================================================
            var activeAudioSourceClips = activeClips.Where(c => c is VideoClip || c is AudioClip).ToList();

            // 비활성화될 오디오 플레이어를 정리합니다.
            var audioClipsToDeactivate = _activeAudioPlayers.Keys.Except(activeAudioSourceClips).ToList();
            foreach (var clip in audioClipsToDeactivate)
            {
                if (_activeAudioPlayers.Remove(clip, out var playerAndEq))
                {
                    var player = playerAndEq.Player;
                    player.Stop();

                    if (player.Media != null)
                    {
                        player.Media.Dispose();
                        player.Media = null;
                    }

                    playerAndEq.Eq.Dispose();
                }
            }


            // 활성화된 오디오 클립들을 처리합니다.
            foreach (var clip in activeAudioSourceClips)
            {
                if (!_activeAudioPlayers.TryGetValue(clip, out var playerAndEq))
                {
                    var player = PlayerViewModel.GetAvailableAudioPlayer();
                    if (player == null) continue;

                    var equalizer = new Equalizer(_flatEqIndex);
                    player.SetEqualizer(equalizer);

                    playerAndEq = (player, equalizer);
                    _activeAudioPlayers.Add(clip, playerAndEq);

                    string mediaPath = (clip is VideoClip vc) ? vc.VideoPath : ((AudioClip)clip).AudioPath;
                    double sourceStartTime = (clip is VideoClip v) ? v.SourceStartTime : ((AudioClip)clip).SourceStartTime;
                    double timeWithinClip = CurrentTimelinePosition - clip.StartPosition;

                    playerAndEq.Player.Media = PlayerViewModel.PrepareMedia(mediaPath, sourceStartTime + (timeWithinClip * clip.SpeedRatio), videoOnly: false, audioOnly: true);
                    playerAndEq.Player.SetRate((float)clip.SpeedRatio);
                    
                }

                int combinedVolume = (int)((clip.Volume / 100.0) * (PlayerViewModel.Volume / 100.0) * 100);
                var preampDb = ConvertVolumeToDb(combinedVolume);
                playerAndEq.Eq.SetPreamp(preampDb);

                if (IsScrubbing || VideoEditor.IsDraggingClip)
                {
                    double sourceStartTime = (clip is VideoClip v) ? v.SourceStartTime : ((AudioClip)clip).SourceStartTime;
                    double timeWithinClip = CurrentTimelinePosition - clip.StartPosition;
                    playerAndEq.Player.Time = (long)((sourceStartTime + (timeWithinClip * clip.SpeedRatio)) * 1000);
                    if (!playerAndEq.Player.IsPlaying) playerAndEq.Player.Play();
                    playerAndEq.Player.SetPause(true);
                }
                else if (IsTimelinePlaying && !playerAndEq.Player.IsPlaying)
                {
                    playerAndEq.Player.Play();
                }
                else if (!IsTimelinePlaying && playerAndEq.Player.IsPlaying)
                {
                    playerAndEq.Player.SetPause(true);
                }
            }

            // ==================================================================
            // 3. 텍스트 요소 처리
            // ==================================================================
            var activeTextClip = activeClips.OfType<TextClip>().OrderByDescending(c => c.TrackIndex).FirstOrDefault();
            IsTextVisible = activeTextClip != null;
            if (activeTextClip != null) { ActiveDisplayText = activeTextClip.Text; }
            UpdateHighlightedVisualClip();
        }

        private float ConvertVolumeToDb(int uiVolume)
        {
            double maxVolumePercentage = 200.0;
            double actualVolumePercentage = (uiVolume / 100.0) * maxVolumePercentage;

            const float minDb = -20.0f;
            const float maxDb = 6.0f;

            if (actualVolumePercentage <= 0) return minDb;

            double linearValue = actualVolumePercentage / 100.0;
            float db = (float)(20 * Math.Log10(linearValue));

            return Math.Clamp(db, minDb, maxDb);
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
