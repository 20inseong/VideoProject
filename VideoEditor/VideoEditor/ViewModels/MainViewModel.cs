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
        public string StatusMessage { get; set; } = "준비 완료";
        public IAsyncRelayCommand ExportVideoCommand { get; }

        public event EventHandler<ExportStartedEventArgs>? ExportStarted;
        public event EventHandler? ExportFinished;
        private Window? _mainWindow;
        private CancellationTokenSource? _exportCts;

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

        private VideoClip? _currentlyPlayingVideo; // 현재 화면에 보이는 비디오 클립
        private readonly Dictionary<AudioClip, MediaPlayer> _activeAudioPlayers = new(); // 현재 소리가 나는 오디오 클립과 그에 할당된 플레이어

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

            PlayerViewModel.MainVideoPlayer.EndReached += OnClipFinished;

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
            if (e.PropertyName == nameof(TimelineClipBase.StartPosition) || e.PropertyName == nameof(VideoClip.Duration))
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
            PlayerViewModel.Stop();
            _currentlyPlayingVideo = null;
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
            _currentlyPlayingVideo = null;
            _activeAudioPlayers.Clear();

            // 1. 플레이해드를 새로운 위치로 이동
            CurrentTimelinePosition = timeSec;

            // 2. 클릭 즉시 새로운 위치에 맞게 플레이어들을 준비 (화면/소리 즉시 변경)
            SyncPlayersToTimeline();

            // 3. 원래 재생 중이었다면, 그 상태를 복원
            if (wasPlaying)
            {
                IsTimelinePlaying = true;
                PlayerViewModel.ResumeAllPlayers(); // 모든 준비된 플레이어 재생 시작
                _timelineTimer.Start();
            }
        }

        private void SyncPlayersToTimeline()
        {
            // 1. 현재 시간대에 활성화된 '모든' 비디오 및 오디오 클립을 찾습니다.
            var activeVideoClips = VideoEditor.TimelineClips.OfType<VideoClip>()
                .Where(c => c.StartPosition <= CurrentTimelinePosition && (c.StartPosition + c.Duration) > CurrentTimelinePosition)
                .ToList();

            var activeAudioClips = VideoEditor.TimelineClips.OfType<AudioClip>()
                .Where(c => c.StartPosition <= CurrentTimelinePosition && (c.StartPosition + c.Duration) > CurrentTimelinePosition)
                .ToList();

            // 2. 비디오 처리: 가장 위에 있는 비디오 클립 하나만 화면에 표시합니다.
            var dominantVideo = activeVideoClips.OrderByDescending(v => v.TrackIndex).FirstOrDefault();

            if (_currentlyPlayingVideo != dominantVideo)
            {
                if (dominantVideo != null) // 새로운 비디오를 재생해야 하는 경우
                {
                    double timeWithinClip = CurrentTimelinePosition - dominantVideo.StartPosition;
                    double seekTimeInSource = dominantVideo.SourceStartTime + timeWithinClip;

                    var media = new Media(PlayerViewModel._libVLC, new Uri(dominantVideo.VideoPath), $":start-time={seekTimeInSource.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

                    PlayerViewModel.MainVideoPlayer.Media = media;
                    if (IsTimelinePlaying) PlayerViewModel.MainVideoPlayer.Play(); // 재생 중일 때만 즉시 재생
                }
                else // 활성화된 비디오가 없는 경우
                {
                    PlayerViewModel.MainVideoPlayer.Stop();
                }
                _currentlyPlayingVideo = dominantVideo;
            }

            // 3. 오디오 믹싱 처리
            var endedAudioClips = _activeAudioPlayers.Keys.Except(activeAudioClips).ToList();
            var newAudioClips = activeAudioClips.Except(_activeAudioPlayers.Keys).ToList();

            // - 재생이 끝난 오디오 클립의 플레이어를 정지하고 반환
            foreach (var endedClip in endedAudioClips)
            {
                if (_activeAudioPlayers.TryGetValue(endedClip, out var player))
                {
                    player.Stop();
                    player.Media = null;
                    _activeAudioPlayers.Remove(endedClip);
                }
            }

            // - 새로 시작해야 하는 오디오 클립에 플레이어를 할당하고 재생
            foreach (var newClip in newAudioClips)
            {
                var availablePlayer = PlayerViewModel.GetAvailableAudioPlayer();
                if (availablePlayer != null)
                {
                    _activeAudioPlayers.Add(newClip, availablePlayer);

                    double timeWithinClip = CurrentTimelinePosition - newClip.StartPosition;
                    double seekTimeInSource = newClip.SourceStartTime + timeWithinClip;

                    var media = new Media(PlayerViewModel._libVLC, new Uri(newClip.AudioPath), $":start-time={seekTimeInSource.ToString(System.Globalization.CultureInfo.InvariantCulture)}");

                    availablePlayer.Media = media;
                    if (IsTimelinePlaying) availablePlayer.Play(); // 재생 중일 때만 즉시 재생
                }
            }
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
            if (PlayerViewModel?.MainVideoPlayer != null)
            {
                PlayerViewModel.MainVideoPlayer.EndReached -= OnClipFinished;
            }
        }
    }
}