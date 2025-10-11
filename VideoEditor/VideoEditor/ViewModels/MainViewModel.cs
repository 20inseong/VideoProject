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

        private readonly Dictionary<TimelineClipBase, MediaPlayer> _activeVisualClipPlayers = new();
        private readonly Dictionary<TimelineClipBase, MediaPlayer> _activeAudioPlayers = new();

        private readonly DispatcherTimer _timelineTimer;

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

        public MainViewModel()
        {
            PlayerViewModel = new PlayerViewModel();
            VideoList = new VideoListViewModel();
            VideoEditor = new VideoEditorViewModel();
            EditorHost = new EditorHostViewModel(PlayerViewModel, VideoEditor);

            var modelPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "ggml-large-v3-turbo-q5_0.bin");
            _speechToTextService = new SpeechToTextService(modelPath);

            VideoEditor.OnClipAdded += MainViewModel_OnClipAdded;

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

            selectedClip.IsTranscribing = true; // Set IsTranscribing on the clip
            IsTranscribing = true; // Global IsTranscribing for overlay
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
                    selectedClip.IsTranscribed = true; // Set IsTranscribed on success
                    selectedClip.ShowTranscription = true; // Show transcription immediately
                }
                StatusMessage = "클립 음성 텍스트 변환 완료.";
            }
            catch (Exception ex)
            {
                StatusMessage = "클립 음성 텍스트 변환 실패.";
                MessageBox.Show($"클립 음성 텍스트 변환 중 오류가 발생했습니다: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                selectedClip.IsTranscribed = false; // Ensure it's false on error
            }
            finally
            {
                selectedClip.IsTranscribing = false; // Always set to false in finally
                IsTranscribing = false;
                TranscriptionProgress = 0;
                OnPropertyChanged(nameof(StatusMessage));
                OnPropertyChanged(nameof(VideoEditor)); // Force UI update for VideoEditor and its properties
            }
        }

        public void UpdateHighlightBorderSize(double playerWidth, double playerHeight)
        {
            // 현재 플레이어의 크기를 내부 변수에 저장해 둡니다.
            _lastPlayerWidth = playerWidth;
            _lastPlayerHeight = playerHeight;

            // 화면에 강조할 클립이 없거나, 플레이어 크기가 0이면 테두리를 숨깁니다 (마진 0).
            if (HighlightedVisualClip == null || playerWidth <= 0 || playerHeight <= 0)
            {
                HighlightBorderMargin = new Thickness(0);
                return;
            }

            int contentWidth = 0;
            int contentHeight = 0;

            // 강조할 클립의 타입에 따라 원본 너비와 높이를 가져옵니다.
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

            // 유효한 콘텐츠 크기가 없으면 테두리를 숨깁니다.
            if (contentWidth <= 0 || contentHeight <= 0)
            {
                HighlightBorderMargin = new Thickness(0);
                return;
            }

            // --- 종횡비 계산 로직 ---
            double playerAspectRatio = playerWidth / playerHeight;
            double contentAspectRatio = (double)contentWidth / contentHeight;

            double marginH = 0; // 좌우 마진 (Pillarbox)
            double marginV = 0; // 상하 마진 (Letterbox)

            if (contentAspectRatio > playerAspectRatio)
            {
                // 콘텐츠가 플레이어보다 가로로 더 넓은 경우 -> 레터박스(상하 검은 바)가 생김
                double scaledHeight = playerWidth / contentAspectRatio;
                marginV = (playerHeight - scaledHeight) / 2.0;
            }
            else
            {
                // 콘텐츠가 플레이어보다 세로로 더 길거나 같은 경우 -> 필러박스(좌우 검은 바)가 생김
                double scaledWidth = playerHeight * contentAspectRatio;
                marginH = (playerWidth - scaledWidth) / 2.0;
            }

            // 계산된 마진 값을 속성에 할당합니다. 이로 인해 PropertyChanged 이벤트가 발생하여 UI가 업데이트됩니다.
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
                    filterComplexBuilder.Append($"[base_v]null[out_v];");
                }

                string amixInputs = string.Join("", audioStreamNamesToMix);
                filterComplexBuilder.Append($"{amixInputs}amix=inputs={audioStreamNamesToMix.Count}[out_a]");

                tempScriptPath = Path.Combine(tempWorkingDirectory, "script.txt");
                await File.WriteAllTextAsync(tempScriptPath, filterComplexBuilder.ToString());

                string safeOutputPath = Path.Combine(tempWorkingDirectory, "output.mp4");

                argumentsBuilder.Append($"-filter_complex_script \"{tempScriptPath}\" ");
                argumentsBuilder.Append($"-map \"[out_v]\" -map \"[out_a]\" ");
                argumentsBuilder.Append($"-c:v libx264 -preset medium -crf 23 -c:a aac -b:a 192k -y \"{safeOutputPath}\"");
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
            if (e.PropertyName == nameof(TimelineClipBase.StartPosition) || e.PropertyName == nameof(TimelineClipBase.Duration))
            {
                UpdateTotalTimelineDuration();
            }
            else if (e.PropertyName == nameof(TimelineClipBase.Volume))
            {
                if (sender is TimelineClipBase changedClip)
                {
                    // Find the active player for this clip and update its volume
                    if (_activeVisualClipPlayers.TryGetValue(changedClip, out var visualPlayer))
                    {
                        visualPlayer.Volume = (int)((changedClip.Volume / 100.0) * (PlayerViewModel.Volume / 100.0) * 100);
                    }
                    if (_activeAudioPlayers.TryGetValue(changedClip, out var audioPlayer))
                    {
                        audioPlayer.Volume = (int)((changedClip.Volume / 100.0) * (PlayerViewModel.Volume / 100.0) * 100);
                    }
                }
            }
        }

        private void PlayerViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(PlayerViewModel.Volume))
            {
                // 전역 볼륨이 변경되면 현재 재생 중인 모든 플레이어의 볼륨을 다시 계산합니다.
                foreach (var (clip, player) in _activeVisualClipPlayers)
                {
                    player.Volume = (int)((clip.Volume / 100.0) * (PlayerViewModel.Volume / 100.0) * 100);
                }
                foreach (var (clip, player) in _activeAudioPlayers)
                {
                    player.Volume = (int)((clip.Volume / 100.0) * (PlayerViewModel.Volume / 100.0) * 100);
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

        private void ExecutePlayPauseTimeline()
        {
            //Debug.WriteLine($"[COMMAND] Play/Pause 버튼 클릭. 현재 재생 상태: {IsTimelinePlaying}");
            if (IsTimelinePlaying)
            {
                _timelineTimer.Stop();
                PlayerViewModel.PauseAllPlayers();
                IsTimelinePlaying = false;
            }
            else
            {
                IsTimelinePlaying = true;
                PlayerViewModel.ResumeAllPlayers();
                _timelineTimer.Start();
            }
        }

        private void ExecuteStopTimeline()
        {
            Debug.WriteLine("[COMMAND] Stop 버튼 클릭.");
            _timelineTimer.Stop();
            PlayerViewModel.Stop(); // 모든 플레이어 정지 및 미디어 해제
            _activeVisualClipPlayers.Clear();
            _activeAudioPlayers.Clear();

            CurrentTimelinePosition = 0;
            IsTimelinePlaying = false;
        }

        public void SeekTimeline(double timeSec)
        {
            Debug.WriteLine($"[SEEK] 타임라인 {timeSec:F2}초로 이동.");

            bool wasPlaying = IsTimelinePlaying;
            if (wasPlaying)
            {
                _timelineTimer.Stop();
                IsTimelinePlaying = false;
            }

            PlayerViewModel.Stop();
            _activeVisualClipPlayers.Clear();
            _activeAudioPlayers.Clear();

            CurrentTimelinePosition = timeSec;

            SyncPlayersToTimeline();

            if (wasPlaying)
            {
                IsTimelinePlaying = true;
                PlayerViewModel.ResumeAllPlayers();
                _timelineTimer.Start();
            }
        }

        private void SyncPlayersToTimeline()
        {
            bool hasClips = VideoEditor.TimelineClips.Any();
            PlayerViewModel.IsControlBarVisible = hasClips;
            if (hasClips)
            {
                PlayerViewModel.VideoViewBackground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Black);
            }
            else
            {
                PlayerViewModel.VideoViewBackground = new System.Windows.Media.SolidColorBrush((System.Windows.Media.Color)System.Windows.Media.ColorConverter.ConvertFromString("#525252"));
            }

            var activeClips = VideoEditor.TimelineClips
                .Where(c => c.StartPosition <= CurrentTimelinePosition && (c.StartPosition + c.Duration) > CurrentTimelinePosition)
                .ToList();

            var activeVisualClips = activeClips
                .Where(c => c is VideoClip || c is ImageClip)
                .OrderBy(c => c.TrackIndex)
                .ToList();

            var activeAudioClips = activeClips.OfType<AudioClip>().ToList();

            var activeTextClip = activeClips
                .OfType<TextClip>()
                .OrderByDescending(c => c.TrackIndex)
                .FirstOrDefault();

            if (activeTextClip != null)
            {
                // 활성화된 자막이 있으면, 내용을 업데이트하고 보이도록 설정합니다.
                ActiveDisplayText = activeTextClip.Text;
                IsTextVisible = true;
            }
            else
            {
                // 활성화된 자막이 없으면, 보이지 않도록 설정합니다。
                IsTextVisible = false;
            }

            var visualClipsToDeactivate = _activeVisualClipPlayers.Keys.Except(activeVisualClips).ToList();
            foreach (var clip in visualClipsToDeactivate)
            {
                if (_activeVisualClipPlayers.TryGetValue(clip, out var player))
                {
                    player.Stop();
                    player.Media?.Dispose();
                    player.Media = null;
                    _activeVisualClipPlayers.Remove(clip);
                }
            }

            for (int trackIndex = 0; trackIndex < PlayerViewModel.VideoPlayers.Count; trackIndex++)
            {
                var player = PlayerViewModel.VideoPlayers[trackIndex];
                var clipForThisTrack = activeVisualClips.FirstOrDefault(c => c.TrackIndex == trackIndex);

                if (clipForThisTrack != null)
                {
                    if (!_activeVisualClipPlayers.ContainsKey(clipForThisTrack))
                    {
                        _activeVisualClipPlayers[clipForThisTrack] = player;

                        double timeWithinClip = CurrentTimelinePosition - clipForThisTrack.StartPosition;
                        double seekTimeInSource = 0; // 이미지 클립의 SourceStartTime은 0으로 간주
                        string mediaPath = string.Empty;

                        if (clipForThisTrack is VideoClip videoClip)
                        {
                            seekTimeInSource = videoClip.SourceStartTime + timeWithinClip;
                            mediaPath = videoClip.VideoPath;
                        }
                        else if (clipForThisTrack is ImageClip imageClip)
                        {
                            mediaPath = imageClip.ImagePath;
                            seekTimeInSource = 0; // 이미지는 시작 시간이 0으로 고정되어 있다고 가정 (LibVLC 이미지 재생)
                        }
                        // TextClip도 시각적 클립이지만, LibVLC MediaPlayer로 직접 표시하지 않습니다.
                        // 따라서 여기서는 VideoClip과 ImageClip만 처리합니다.

                        if (!string.IsNullOrEmpty(mediaPath))
                        {
                            player.Media = PlayerViewModel.PrepareMedia(mediaPath, seekTimeInSource, false);
                            // ✨ 클립별 볼륨과 전역 볼륨을 조합하여 최종 볼륨 설정
                            player.Volume = (int)((clipForThisTrack.Volume / 100.0) * (PlayerViewModel.Volume / 100.0) * 100);
                            if (IsTimelinePlaying) player.Play();
                        }
                    }
                    // (선택 사항) 이미 재생 중인 클립이더라도, 타임라인을 수동으로 탐색했을 때 정확한 위치로 이동시키려면
                    // player.SetTime((long)(seekTimeInSource * 1000)); 와 같은 로직을 추가할 수 있습니다.
                    // 하지만 현재는 ClipAdded/Stop/SeekTimeline에서 플레이어를 재설정하므로
                    // 여기서는 필요하지 않을 수 있습니다.
                }
                else
                {
                    if (player.Media != null)
                    {
                        player.Stop();
                        player.Media.Dispose();
                        player.Media = null;
                    }
                }
            }
            var desiredAudioSources = new List<TimelineClipBase>();
            desiredAudioSources.AddRange(activeAudioClips);

            var audioToDeactivate = _activeAudioPlayers.Keys.Except(desiredAudioSources).ToList();
            foreach (var clip in audioToDeactivate)
            {
                if (_activeAudioPlayers.TryGetValue(clip, out var player))
                {
                    player.Stop();
                    player.Media?.Dispose();
                    player.Media = null;
                    _activeAudioPlayers.Remove(clip);
                }
            }

            foreach (var clip in desiredAudioSources)
            {
                if (_activeAudioPlayers.ContainsKey(clip)) continue;

                var player = PlayerViewModel.GetAvailableAudioPlayer();
                if (player == null) continue;

                _activeAudioPlayers.Add(clip, player);

                var audioClip = (AudioClip)clip;
                string path = audioClip.AudioPath;
                double sourceStartTime = audioClip.SourceStartTime;

                double timeWithinClip = CurrentTimelinePosition - clip.StartPosition;
                double seekTimeInSource = sourceStartTime + timeWithinClip;

                player.Media = PlayerViewModel.PrepareMedia(path, seekTimeInSource, true);
                // ✨ 클립별 볼륨과 전역 볼륨을 조합하여 최종 볼륨 설정
                player.Volume = (int)((clip.Volume / 100.0) * (PlayerViewModel.Volume / 100.0) * 100);
                if (IsTimelinePlaying) player.Play();
            }
            UpdateHighlightedVisualClip();
        }

        private async void OnClipFinished(object? sender, EventArgs e)
        {
            if (VideoEditor.CurrentlyPlayingClip != null)
            {
                Debug.WriteLine($"'{VideoEditor.CurrentlyPlayingClip.Name}' 클립 재생 완료. 마스터 루프가 계속 진행합니다.");
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

            //Debug.WriteLine($"[Timeline Duration] 총 타임라인 길이 업데이트: {TotalTimelineDurationMs / 1000.0:F2}초");
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