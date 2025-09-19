using CommunityToolkit.Mvvm.Input;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using VideoEditor.Common;
using VideoEditor.Models;
using WpfMedia = System.Windows.Media;

namespace VideoEditor.ViewModels
{
    public class ExportStartedEventArgs : EventArgs
    {
        public ExportProgressViewModel ProgressViewModel { get; }
        public ExportStartedEventArgs(ExportProgressViewModel viewModel) { ProgressViewModel = viewModel; }
    }

    public class MainViewModel : ViewModelBase, IDisposable
    {
        public PlayerViewModel PlayerViewModel { get; }
        public VideoListViewModel VideoList { get; }
        public VideoEditorViewModel VideoEditor { get; }
        public string StatusMessage { get; set; } = "준비 완료";
        public IAsyncRelayCommand ExportVideoCommand { get; }

        public event EventHandler<ExportStartedEventArgs>? ExportStarted;
        public event EventHandler? ExportFinished;
        private Window? _mainWindow;
        private CancellationTokenSource? _exportCts;

        private readonly DispatcherTimer _gapTimer;
        private DateTime _lastTimerTick;

        private MediaLayerViewModel? _masterClockSource;

        public MainViewModel(Window mainWindow) : this() { _mainWindow = mainWindow; }

        private WpfMedia.Brush _playerBackground = new WpfMedia.SolidColorBrush((WpfMedia.Color)WpfMedia.ColorConverter.ConvertFromString("#525252"));
        public WpfMedia.Brush PlayerBackground { get => _playerBackground; set => SetProperty(ref _playerBackground, value); }
        private bool _isControlBarVisible;
        public bool IsControlBarVisible { get => _isControlBarVisible; private set => SetProperty(ref _isControlBarVisible, value); }
        private bool _isTimelinePlaying;
        public bool IsTimelinePlaying { get => _isTimelinePlaying; private set { if (SetProperty(ref _isTimelinePlaying, value)) OnPropertyChanged(nameof(PlayPauseButtonContent)); } }
        public string PlayPauseButtonContent => IsTimelinePlaying ? "❚❚" : "▶";
        public IRelayCommand PlayPauseTimelineCommand { get; }
        public IRelayCommand StopTimelineCommand { get; }
        private double _currentTimelinePosition;
        public double CurrentTimelinePosition { get; set; }
        public long CurrentTimelineTimeMs { get => (long)(CurrentTimelinePosition * 1000); set => SeekTimeline(value / 1000.0); }
        private long _totalTimelineDurationMs;
        public long TotalTimelineDurationMs { get => _totalTimelineDurationMs; private set => SetProperty(ref _totalTimelineDurationMs, value); }
        private bool _isExporting;
        public bool IsExporting { get => _isExporting; set => SetProperty(ref _isExporting, value); }
        private double _exportProgress;
        public double ExportProgress { get => _exportProgress; set => SetProperty(ref _exportProgress, value); }

        public MainViewModel()
        {
            PlayerViewModel = new PlayerViewModel();
            VideoList = new VideoListViewModel();
            VideoEditor = new VideoEditorViewModel();

            VideoEditor.TimelineClips.CollectionChanged += (s, e) =>
            {
                if (e.Action == NotifyCollectionChangedAction.Remove)
                {
                    foreach (TimelineClipBase item in e.OldItems)
                    {
                        item.PropertyChanged -= Clip_PropertyChanged;
                        PlayerViewModel.RemoveLayerFromCache(item.Id);
                    }
                }
                if (e.NewItems != null)
                {
                    foreach (TimelineClipBase item in e.NewItems) item.PropertyChanged += Clip_PropertyChanged;
                }
                UpdateTotalTimelineDuration();
                UpdateTimelineState();
            };

            PlayPauseTimelineCommand = new RelayCommand(ExecutePlayPauseTimeline);
            StopTimelineCommand = new RelayCommand(ExecuteStopTimeline);
            ExportVideoCommand = new AsyncRelayCommand(StartExportProcessAsync);

            _gapTimer = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(30) };
            _gapTimer.Tick += OnGapTimerTick;

            UpdateTotalTimelineDuration();
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
            //var progressViewModel = new ExportProgressViewModel(() => _exportCts.Cancel());
            //ExportStarted?.Invoke(this, new ExportStartedEventArgs(progressViewModel));

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
                                        progressViewModel.Progress = Math.Min(100, progress); // 100%를 넘지 않도록 합니다.
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

        private void OnGapTimerTick(object? sender, EventArgs e)
        {
            if (_masterClockSource != null) return; // 마스터 클럭이 있으면 보조 타이머는 쉽니다.

            var now = DateTime.UtcNow;
            var elapsed = now - _lastTimerTick;
            _lastTimerTick = now;
            CurrentTimelinePosition += elapsed.TotalSeconds * PlayerViewModel.PlaybackRate;
            OnPropertyChanged(nameof(CurrentTimelinePosition));
            OnPropertyChanged(nameof(CurrentTimelineTimeMs));

            if (CurrentTimelinePosition * 1000 >= TotalTimelineDurationMs)
            {
                ExecuteStopTimeline();
                return;
            }
            UpdateTimelineState(); // 새로운 클립을 만났는지 계속 확인
        }

        private void Clip_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TimelineClipBase.StartPosition) || e.PropertyName == nameof(TimelineClipBase.Duration))
            {
                UpdateTotalTimelineDuration();
            }
        }

        private void ExecutePlayPauseTimeline()
        {
            if (IsTimelinePlaying)
            {
                IsTimelinePlaying = false;
                _gapTimer.Stop();
                PlayerViewModel.PauseAllActive();
            }
            else
            {
                IsTimelinePlaying = true;
                UpdateTimelineState();
            }
        }

        private void ExecuteStopTimeline()
        {
            IsTimelinePlaying = false;
            _gapTimer.Stop();
            SetMasterClockSource(null);
            PlayerViewModel.StopAndResetAll();
            CurrentTimelinePosition = 0;
            OnPropertyChanged(nameof(CurrentTimelinePosition));
            OnPropertyChanged(nameof(CurrentTimelineTimeMs));
            UpdateTimelineState();
        }

        public void SeekTimeline(double timeSec)
        {
            bool wasPlaying = IsTimelinePlaying;
            if (wasPlaying) IsTimelinePlaying = false;

            CurrentTimelinePosition = Math.Clamp(timeSec, 0, TotalTimelineDurationMs / 1000.0);
            OnPropertyChanged(nameof(CurrentTimelinePosition));
            OnPropertyChanged(nameof(CurrentTimelineTimeMs));

            UpdateTimelineState();

            if (wasPlaying) IsTimelinePlaying = true;

            if (IsTimelinePlaying) UpdateTimelineState();
        }

        private void UpdateTimelineState()
        {
            var activeClips = VideoEditor.TimelineClips
                .Where(c => c.StartPosition <= CurrentTimelinePosition && (c.StartPosition + c.Duration) > CurrentTimelinePosition)
                .ToList();

            var activeClipIds = activeClips.Select(c => c.Id).ToHashSet();
            var layersToRemove = PlayerViewModel.Layers.Where(l => !activeClipIds.Contains(l.SourceClip.Id)).ToList();

            foreach (var layer in layersToRemove) PlayerViewModel.Layers.Remove(layer);
            foreach (var clip in activeClips)
            {
                var layer = PlayerViewModel.GetOrCreateLayer(clip);
                if (!PlayerViewModel.Layers.Contains(layer)) PlayerViewModel.Layers.Add(layer);
            }

            foreach (var layer in PlayerViewModel.Layers)
            {
                layer.Sync(CurrentTimelinePosition, IsTimelinePlaying);
            }

            var newMasterClip = activeClips.OfType<VideoClip>().OrderByDescending(c => c.TrackIndex).FirstOrDefault()
                              ?? (TimelineClipBase?)activeClips.OfType<AudioClip>().OrderByDescending(c => c.TrackIndex).FirstOrDefault();

            SetMasterClockSource(newMasterClip != null ? PlayerViewModel.GetOrCreateLayer(newMasterClip) : null);
        }

        private void UpdateTotalTimelineDuration()
        {
            bool hasClips = VideoEditor.TimelineClips.Any();
            if (hasClips)
            {
                double maxEndTimeSec = VideoEditor.TimelineClips.Max(c => c.StartPosition + c.Duration);
                TotalTimelineDurationMs = (long)(maxEndTimeSec * 1000);
                PlayerBackground = new WpfMedia.SolidColorBrush(WpfMedia.Colors.Black);
            }
            else
            {
                TotalTimelineDurationMs = 300 * 1000;
                PlayerBackground = new WpfMedia.SolidColorBrush((WpfMedia.Color)WpfMedia.ColorConverter.ConvertFromString("#525252"));
            }
            IsControlBarVisible = hasClips;
        }

        private void SetMasterClockSource(MediaLayerViewModel? newMaster)
        {
            if (_masterClockSource == newMaster) return;
            if (_masterClockSource != null) _masterClockSource.MediaPlayer.TimeChanged -= MasterClock_TimeChanged;

            _masterClockSource = newMaster;

            if (_masterClockSource != null)
            {
                _gapTimer.Stop(); // 왕이 있으니 보조는 멈춘다.
                _masterClockSource.MediaPlayer.TimeChanged += MasterClock_TimeChanged;
            }
            else if (IsTimelinePlaying)
            {
                _lastTimerTick = DateTime.UtcNow;
                _gapTimer.Start(); // 왕이 없으니 보조가 나선다.
            }
        }

        private void MasterClock_TimeChanged(object? sender, MediaPlayerTimeChangedEventArgs e)
        {
            if (!IsTimelinePlaying || _masterClockSource == null) return;

            double timeWithinClip = e.Time / 1000.0;
            double sourceStartTime = 0;
            if (_masterClockSource.SourceClip is VideoClip vc) sourceStartTime = vc.SourceStartTime;
            else if (_masterClockSource.SourceClip is AudioClip ac) sourceStartTime = ac.SourceStartTime;
            double newPosition = (_masterClockSource.SourceClip.StartPosition + timeWithinClip) - sourceStartTime;

            UIDispatcher.Invoke(() =>
            {
                CurrentTimelinePosition = newPosition;
                OnPropertyChanged(nameof(CurrentTimelinePosition));
                OnPropertyChanged(nameof(CurrentTimelineTimeMs));

                UpdateTimelineState();
            });
        }

        public void Dispose()
        {
            _gapTimer.Stop();
            _exportCts?.Cancel();
            _exportCts?.Dispose();
            PlayerViewModel?.Dispose();
            VideoEditor?.Dispose();
        }
    }
}