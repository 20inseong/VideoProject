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
        public string StatusMessage { get; set; } = "준비 완료";
        public IAsyncRelayCommand ExportVideoCommand { get; }

        public event EventHandler<ExportStartedEventArgs>? ExportStarted;
        public event EventHandler? ExportFinished;
        private Window? _mainWindow;

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
                    if (e.NewItems != null)
                    {
                        foreach (VideoClip newClip in e.NewItems)
                        {
                            newClip.PropertyChanged += Clip_PropertyChanged;
                        }
                    }
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
            ExportVideoCommand = new AsyncRelayCommand(StartExportProcessAsync);

            PlayerViewModel.MediaPlayer.EndReached += OnClipFinished;

            _timelineTimer = new DispatcherTimer(DispatcherPriority.Render)
            {
                Interval = TimeSpan.FromMilliseconds(50)
            };
            _timelineTimer.Tick += OnTimelineTimerTick;

            UpdateTotalTimelineDuration();
        }

        private async Task StartExportProcessAsync()
        {
            Debug.WriteLine("[DEBUG] StartExportProcessAsync method has started.");

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
                return;
            }

            string outputPath = saveFileDialog.FileName;

            Debug.WriteLine($"[DEBUG] Path selected. Preparing to call RunExportLogicAsync with path: {outputPath}");
            await RunExportLogicAsync(outputPath);
            Debug.WriteLine("[DEBUG] RunExportLogicAsync has completed.");
        }

        private async Task RunExportLogicAsync(string outputPath)
        {
            if (!VideoEditor.TimelineClips.Any())
            {
                Debug.WriteLine("[EXPORT] Error: No clips on the timeline.");
                StatusMessage = "내보낼 클립이 타임라인에 없습니다.";
                OnPropertyChanged(nameof(StatusMessage));
                return;
            }

            Debug.WriteLine($"[EXPORT] User's desired output path: {outputPath}");
            var progressViewModel = new ExportProgressViewModel();
            ExportStarted?.Invoke(this, new ExportStartedEventArgs(progressViewModel));

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

                var uniqueSourceFiles = VideoEditor.TimelineClips.Select(c => c.VideoPath).Distinct().ToList();

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
                    string safeClipPath = safePathMappings[clip.VideoPath];
                    int fileIndex = safeInputFiles.IndexOf(safeClipPath);

                    string sourceStartTime = clip.SourceStartTime.ToString("F6", CultureInfo.InvariantCulture);
                    string duration = clip.Duration.ToString("F6", CultureInfo.InvariantCulture);

                    string videoDelayTime = clip.StartPosition.ToString("F6", CultureInfo.InvariantCulture);
                    var audioDelayTimeMs = (long)(clip.StartPosition * 1000);

                    string videoTrimmed = $"[v_trimmed{i}]";
                    string videoDelayed = $"[v_delayed{i}]";
                    filterComplexBuilder.Append($"[{fileIndex}:v]trim=start={sourceStartTime}:duration={duration},setpts=PTS-STARTPTS,scale={outputResolution},setsar=1{videoTrimmed};");
                    filterComplexBuilder.Append($"{videoTrimmed}setpts=PTS+{videoDelayTime}/TB{videoDelayed};");
                    videoStreamNamesToOverlay.Add(videoDelayed);

                    filterComplexBuilder.Append($"[{fileIndex}:a]atrim=start={sourceStartTime}:duration={duration},asetpts=PTS-STARTPTS[a_trimmed{i}];");
                    filterComplexBuilder.Append($"[a_trimmed{i}]adelay={audioDelayTimeMs}|{audioDelayTimeMs}[a{i}];");
                    audioStreamNamesToMix.Add($"[a{i}]");
                }

                string lastVideoOutput = "[base_v]";
                for (int i = 0; i < videoStreamNamesToOverlay.Count; i++)
                {
                    string newVideoOutput = (i == videoStreamNamesToOverlay.Count - 1) ? "[out_v]" : $"[v_merged{i}]";
                    string streamToOverlay = videoStreamNamesToOverlay[i];

                    filterComplexBuilder.Append($"{lastVideoOutput}{streamToOverlay}overlay=x=0:y=0:eof_action=pass{newVideoOutput}");

                    if (i < videoStreamNamesToOverlay.Count - 1)
                    {
                        filterComplexBuilder.Append(";");
                        lastVideoOutput = newVideoOutput;
                    }
                }

                string amixInputs = string.Join("", audioStreamNamesToMix);
                filterComplexBuilder.Append($";{amixInputs}amix=inputs={audioStreamNamesToMix.Count}[out_a]");

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
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = new Process { StartInfo = processStartInfo })
                {
                    process.ErrorDataReceived += (sender, args) =>
                    {
                        if (string.IsNullOrWhiteSpace(args.Data)) return;
                        Debug.WriteLine($"[FFMPEG LOG]: {args.Data}");
                    };

                    Debug.WriteLine("[EXPORT] Starting FFmpeg process...");
                    process.Start();
                    process.BeginErrorReadLine();

                    await process.WaitForExitAsync();

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

            var clipToPlay = VideoEditor.TimelineClips
                .FirstOrDefault(c => c.StartPosition <= CurrentTimelinePosition && (c.StartPosition + c.Duration) > CurrentTimelinePosition);

            if (clipToPlay != null && VideoEditor.CurrentlyPlayingClip != clipToPlay)
            {
                Debug.WriteLine($"[Timeline Tick] '{clipToPlay.Name}' 재생 시작.");
                VideoEditor.CurrentlyPlayingClip = clipToPlay;

                double timeWithinClip = CurrentTimelinePosition - clipToPlay.StartPosition;
                double seekTimeInSource = clipToPlay.SourceStartTime + timeWithinClip;

                PlayerViewModel.PlayMediaFrom(clipToPlay.VideoPath, (long)(seekTimeInSource * 1000));
            }
            else if (clipToPlay == null && VideoEditor.CurrentlyPlayingClip != null)
            {
                Debug.WriteLine("[Timeline Tick] 빈 공간(Gap) 진입. 재생을 멈춥니다.");
                PlayerViewModel.Stop();
                VideoEditor.CurrentlyPlayingClip = null;
            }
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
                //Debug.WriteLine($"[EVENT] 첫 클립 추가됨: {e.VideoPath}. 미리보기를 위해 로드합니다.");
                PlayerViewModel.LoadMedia(e.VideoPath);
            }
        }

        private void ExecutePlayPauseTimeline()
        {
            //Debug.WriteLine($"[COMMAND] Play/Pause 버튼 클릭. 현재 재생 상태: {IsTimelinePlaying}");
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
            //Debug.WriteLine($"[SEEK] 타임라인 {timeSec:F2}초로 이동.");

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

            //Debug.WriteLine($"[Timeline Duration] 총 타임라인 길이 업데이트: {TotalTimelineDurationMs / 1000.0:F2}초");
        }
    }
}