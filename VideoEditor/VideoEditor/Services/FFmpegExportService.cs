using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VideoEditor.Models;
using VideoEditor.Common;
using VideoEditor.ViewModels;

namespace VideoEditor.Services
{
    public class FFmpegExportService
    {
        private const double OutputWidth = 1920.0;
        private const double OutputHeight = 1080.0;

        private const double PreviewWidth = 800.0;
        private const double PreviewHeight = 450.0;

        private readonly string _ffmpegPath;

        public FFmpegExportService()
        {
            _ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "bin", "ffmpeg.exe");
        }

        public async Task<bool> ExportVideoAsync(
            ICollection<TimelineClipBase> clips,
            double totalDurationSeconds,
            string outputPath,
            ExportProgressViewModel progressViewModel,
            CancellationToken cancellationToken)
        {
            if (!clips.Any())
            {
                progressViewModel.StatusMessage = "오류: 타임라인에 내보낼 클립이 없습니다.";
                return false;
            }
            if (!File.Exists(_ffmpegPath))
            {
                progressViewModel.StatusMessage = $"오류: ffmpeg.exe를 찾을 수 없습니다. 경로: {_ffmpegPath}";
                return false;
            }

            var tempDirectory = Path.Combine(Path.GetTempPath(), "VideoEditorExport", Guid.NewGuid().ToString());
            Directory.CreateDirectory(tempDirectory);
            var tempOutputPath = Path.Combine(tempDirectory, "output.mp4");

            try
            {
                progressViewModel.StatusMessage = "내보내기 준비 중...";
                var (inputArguments, filterComplex) = await BuildFFmpegArguments(clips, totalDurationSeconds, tempDirectory);

                var tempScriptPath = Path.Combine(tempDirectory, "filter_script.txt");
                await File.WriteAllTextAsync(tempScriptPath, filterComplex, cancellationToken);

                var arguments = new StringBuilder();
                arguments.Append(inputArguments);
                arguments.Append($"-filter_complex_script \"{tempScriptPath}\" ");
                arguments.Append("-map \"[final_v]\" -map \"[final_a]\" ");
                arguments.Append($"-c:v libx264 -preset medium -crf 23 -c:a aac -b:a 192k -y \"{tempOutputPath}\"");

                //Debug.WriteLine("--- FFmpeg DEBUG START ---");
                //Debug.WriteLine("Final FFmpeg Command:");
                //Debug.WriteLine($"{_ffmpegPath} {arguments}");
                //Debug.WriteLine("\nFilter Script Content:");
                //Debug.WriteLine(filterComplex);
                //Debug.WriteLine("--- FFmpeg DEBUG END ---");

                bool success = await RunFFmpegProcess(arguments.ToString(), totalDurationSeconds, progressViewModel, cancellationToken);

                if (success) { File.Move(tempOutputPath, outputPath, true); }
                return success;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[EXPORT CRITICAL ERROR] {ex.Message}");
                progressViewModel.StatusMessage = $"치명적인 오류 발생: {ex.Message}";
                return false;
            }
            finally
            {
                try { if (Directory.Exists(tempDirectory)) { Directory.Delete(tempDirectory, true); } }
                catch (Exception ex) { Debug.WriteLine($"[EXPORT] Failed to clean up temp directory: {tempDirectory}. Reason: {ex.Message}"); }
            }
        }

        private async Task<(string inputArguments, string filterComplex)> BuildFFmpegArguments(
            ICollection<TimelineClipBase> clips,
            double totalDurationSeconds,
            string tempDirectory)
        {

#if DEBUG
            Debug.WriteLine("\n--- BEGIN TIMELINE CLIP DATA DUMP (Passed to FFmpeg Service) ---");
            foreach (var clip in clips.OrderBy(c => c.StartPosition))
            {
                var sb = new StringBuilder();
                sb.AppendLine($"Clip: '{clip.Name}' ({clip.GetType().Name})");
                sb.AppendLine($"  - StartPosition: {clip.StartPosition:F3}s");
                sb.AppendLine($"  - Duration: {clip.Duration:F3}s");
                sb.AppendLine($"  - SpeedRatio: {clip.SpeedRatio:F2}x");

                if (clip is ImageClip ic)
                {
                    sb.AppendLine($"  - Source W/H: {ic.SourceWidth}x{ic.SourceHeight}");
                    sb.AppendLine($"  - Render W/H: {ic.RenderWidth:F2}x{ic.RenderHeight:F2}"); // 실시간 크기
                    sb.AppendLine($"  - Position X/Y: {ic.X:F2}, {ic.Y:F2}");                   // 실시간 위치
                    sb.AppendLine($"  - Stored Scale: {ic.Scale:P2}");                          // 저장된 배율
                }

                else if (clip is VideoClip vc)
                {
                    sb.AppendLine($"  - SourceResolution: {vc.SourceWidth}x{vc.SourceHeight}");
                }

                sb.AppendLine($"  - Transform: Scale={clip.Scale:P0}, X={clip.X}, Y={clip.Y}");
                Debug.WriteLine(sb.ToString());
            }
            Debug.WriteLine("--- END TIMELINE CLIP DATA DUMP ---\n");
#endif

            var inputFiles = clips.Select(c => c switch {
                VideoClip vc => vc.VideoPath,
                AudioClip ac => ac.AudioPath,
                ImageClip ic => ic.ImagePath,
                _ => null
            }).Where(path => !string.IsNullOrEmpty(path)).Distinct().ToList();

            var inputArguments = new StringBuilder();
            for (int i = 0; i < inputFiles.Count; i++) { inputArguments.Append($"-i \"{inputFiles[i]}\" "); }

            var filterComplex = new StringBuilder();
            var culture = CultureInfo.InvariantCulture;
            string outputResolution = $"{(int)OutputWidth}x{(int)OutputHeight}";
            string outputFrameRate = "30";
            string audioSampleRate = "44100";

            filterComplex.AppendLine($"color=c=black:s={outputResolution}:r={outputFrameRate}:d={totalDurationSeconds.ToString("F6", culture)}[base_v];");
            filterComplex.AppendLine($"anullsrc=r={audioSampleRate}:cl=stereo:d={totalDurationSeconds.ToString("F6", culture)}[base_a];");

            var audioStreamsToMix = new List<string> { "[base_a]" };
            var clipIdToProcessedStreamMap = new Dictionary<Guid, string>();

            foreach (var clip in clips)
            {
                string clipId = clip.Id.ToString("N");
                switch (clip)
                {
                    case VideoClip vc:
                        {
                            int fileIndex = inputFiles.IndexOf(vc.VideoPath);
                            double sourceDuration = vc.Duration * vc.SpeedRatio;

                            string normalizeFilter = $"scale={OutputWidth}:{OutputHeight}:force_original_aspect_ratio=decrease";
                            string userScaleFilter = $"scale=iw*{vc.Scale}:ih*{vc.Scale}";

                            string speedAndTrimFilter = $"trim=start={vc.SourceStartTime.ToString("F6", culture)}:duration={sourceDuration.ToString("F6", culture)}, " +
                            $"setpts=PTS-STARTPTS, " +
                            $"setpts=PTS/({vc.SpeedRatio.ToString("F6", culture)}), " +
                            $"setpts=PTS+({vc.StartPosition.ToString("F6", culture)})/TB, ";

                            filterComplex.AppendLine($"[{fileIndex}:v] {speedAndTrimFilter}{normalizeFilter}, {userScaleFilter}, setsar=1 [processed_{clipId}];");
                            clipIdToProcessedStreamMap[vc.Id] = $"[processed_{clipId}]";

                            if (!vc.IsMuted)
                            {
                                filterComplex.AppendLine($"[{fileIndex}:a] atrim=start={vc.SourceStartTime.ToString("F6", culture)}:duration={sourceDuration.ToString("F6", culture)},asetpts=PTS-STARTPTS, " +
                                    $"{BuildAtempoFilter(vc.SpeedRatio)}, volume={(vc.Volume / 100.0).ToString("F2", culture)}, " +
                                    $"adelay={(long)(vc.StartPosition * 1000)}|{(long)(vc.StartPosition * 1000)} [a_{clipId}];");
                                audioStreamsToMix.Add($"[a_{clipId}]");
                            }
                            break;
                        }
                    case ImageClip ic:
                        {
                            int fileIndex = inputFiles.IndexOf(ic.ImagePath);

                            double scaleX = OutputWidth / PreviewWidth;
                            double scaleY = OutputHeight / PreviewHeight;

                            double targetWidth = ic.RenderWidth * scaleX;
                            double targetHeight = ic.RenderHeight * scaleY;

                            string userResizeFilter = $"scale={targetWidth.ToString("F0", culture)}:{targetHeight.ToString("F0", culture)}";
                            string videoStreamFilter = $"loop=loop=-1:size=1,trim=duration={totalDurationSeconds.ToString("F6", culture)},setpts=PTS-STARTPTS,";

                            filterComplex.AppendLine($"[{fileIndex}:v] {videoStreamFilter} {userResizeFilter}, setsar=1 [processed_{clipId}];");
                            clipIdToProcessedStreamMap[ic.Id] = $"[processed_{clipId}]";
                            break;
                        }
                    case AudioClip audioClip:
                        {
                            int fileIndex = inputFiles.IndexOf(audioClip.AudioPath);
                            // <--- 수정됨: 속도 배율을 고려한 실제 원본 클립 길이 계산
                            double sourceDuration = audioClip.Duration * audioClip.SpeedRatio;

                            // <--- 수정됨: atrim의 duration을 sourceDuration으로 변경
                            filterComplex.AppendLine($"[{fileIndex}:a] atrim=start={audioClip.SourceStartTime.ToString("F6", culture)}:duration={sourceDuration.ToString("F6", culture)},asetpts=PTS-STARTPTS, " +
                                $"{BuildAtempoFilter(audioClip.SpeedRatio)}, volume={(audioClip.Volume / 100.0).ToString("F2", culture)}, " +
                                $"adelay={(long)(audioClip.StartPosition * 1000)}|{(long)(audioClip.StartPosition * 1000)} [a_{clipId}];");
                            audioStreamsToMix.Add($"[a_{clipId}]");
                            break;
                        }
                    case TextClip tc:
                        {
                            clipIdToProcessedStreamMap[tc.Id] = "text_clip";
                            break;
                        }
                }
            }

            var sortedClips = clips.Where(c => clipIdToProcessedStreamMap.ContainsKey(c.Id)).OrderByDescending(c => c.TrackIndex).ToList();
            string lastVideoStream = "[base_v]";
            int overlayCounter = 0;

            double positionScaleX = OutputWidth / PreviewWidth;
            double positionScaleY = OutputHeight / PreviewHeight;

            // 폰트 폴더를 수집해서 주소를 변형해줘야 할 듯 싶습니다.
            string fontPath = "C:/Windows/Fonts/Arial.ttf".Replace(":", "\\:");

            var overlayClips = clips
                .Where(c => (c is VideoClip || c is ImageClip) && clipIdToProcessedStreamMap.ContainsKey(c.Id))
                .OrderByDescending(c => c.TrackIndex)
                .ToList();

            var textClips = clips
                .OfType<TextClip>()
                .OrderByDescending(c => c.TrackIndex) // 텍스트끼리의 순서도 중요할 수 있음
                .ToList();

            foreach (var clip in overlayClips)
            {
                string nextVideoStream = $"[final_v_{overlayCounter}]";

                if (clip is VideoClip videoClip)
                {
                    // [비디오: 중앙 기준]
                    string streamToOverlay = clipIdToProcessedStreamMap[videoClip.Id];
                    double targetX_offset = videoClip.X * positionScaleX;
                    double targetY_offset = videoClip.Y * positionScaleY;
                    string ffmpegX = $"(main_w-overlay_w)/2 + {targetX_offset.ToString("F2", culture)}";
                    string ffmpegY = $"(main_h-overlay_h)/2 + {targetY_offset.ToString("F2", culture)}";
                    string enableOption = $"enable='between(t,{videoClip.StartPosition.ToString("F6", culture)},{(videoClip.StartPosition + videoClip.Duration).ToString("F6", culture)})'";
                    filterComplex.AppendLine($"{lastVideoStream}{streamToOverlay} overlay=x='{ffmpegX}':y='{ffmpegY}':{enableOption} {nextVideoStream};");
                }
                else if (clip is ImageClip imageClip)
                {
                    // [이미지: 좌측 상단 기준]
                    string streamToOverlay = clipIdToProcessedStreamMap[imageClip.Id];
                    double targetX = imageClip.X * positionScaleX;
                    double targetY = imageClip.Y * positionScaleY;
                    string ffmpegX = targetX.ToString("F2", culture);
                    string ffmpegY = targetY.ToString("F2", culture);
                    string enableOption = $"enable='between(t,{imageClip.StartPosition.ToString("F6", culture)},{(imageClip.StartPosition + imageClip.Duration).ToString("F6", culture)})'";
                    filterComplex.AppendLine($"{lastVideoStream}{streamToOverlay} overlay=x={ffmpegX}:y={ffmpegY}:{enableOption} {nextVideoStream};");
                }

                lastVideoStream = nextVideoStream;
                overlayCounter++;
            }

            foreach (var textClip in textClips)
            {
                string nextVideoStream = $"[final_v_{overlayCounter}]";

                double targetX = textClip.X * positionScaleX;
                double targetY = textClip.Y * positionScaleY;
                string ffmpegX_text = targetX.ToString("F2", culture);
                string ffmpegY_text = targetY.ToString("F2", culture);

                double targetFontSize = textClip.RenderHeight * positionScaleY;
                double finalFontSize = targetFontSize * 0.8;

                string enable_text = $"enable='between(t,{textClip.StartPosition.ToString("F6", culture)},{(textClip.StartPosition + textClip.Duration).ToString("F6", culture)})'";
                string escapedText = textClip.Text.Replace("'", @"\'").Replace(":", @"\:").Replace("%", @"\%");

                filterComplex.AppendLine($"{lastVideoStream} " +
                    $"drawtext=fontfile='{fontPath}':text='{escapedText}':" +
                    $"x={ffmpegX_text}:y={ffmpegY_text}:fontsize={finalFontSize.ToString("F0", culture)}:fontcolor=white:box=1:boxcolor=black@0.5:boxborderw=5:" +
                    $"{enable_text} {nextVideoStream};");

                lastVideoStream = nextVideoStream;
                overlayCounter++;
            }
            filterComplex.AppendLine($"{lastVideoStream}trim=duration={totalDurationSeconds.ToString("F6", culture)}[final_v];");
            filterComplex.AppendLine($"{string.Join("", audioStreamsToMix)}amix=inputs={audioStreamsToMix.Count}:duration=first:dropout_transition=3[final_a];");
            return (inputArguments.ToString(), filterComplex.ToString());
        }

        private string BuildAtempoFilter(double speedRatio)
        {
            if (speedRatio == 1.0) return "anull";
            var tempoFilters = new List<string>();
            double currentSpeed = speedRatio;
            if (currentSpeed < 0.5) { while (currentSpeed < 0.5) { tempoFilters.Add("atempo=0.5"); currentSpeed /= 0.5; } }
            else if (currentSpeed > 2.0) { while (currentSpeed > 2.0) { tempoFilters.Add("atempo=2.0"); currentSpeed /= 2.0; } }
            if (currentSpeed > 0) tempoFilters.Add($"atempo={currentSpeed.ToString("F6", CultureInfo.InvariantCulture)}");
            return string.Join(",", tempoFilters);
        }

        private async Task<bool> RunFFmpegProcess(string arguments, double totalDurationSeconds, ExportProgressViewModel progressViewModel, CancellationToken cancellationToken)
        {
            var pStartInfo = new ProcessStartInfo
            {
                FileName = _ffmpegPath,
                Arguments = arguments,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8
            };
            using var process = new Process { StartInfo = pStartInfo };
            process.ErrorDataReceived += (s, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;
                //Debug.WriteLine($"[FFMPEG LOG]: {e.Data}");
                if (e.Data.Contains("time="))
                {
                    try
                    {
                        var timeStr = e.Data.Split(new[] { "time=" }, StringSplitOptions.None)[1].Split(' ')[0];
                        if (TimeSpan.TryParse(timeStr, CultureInfo.InvariantCulture, out var currTime))
                        {
                            double progress = (currTime.TotalSeconds / totalDurationSeconds) * 100;
                            UIDispatcher.Invoke(() =>
                            {
                                progressViewModel.Progress = Math.Min(100, progress);
                                progressViewModel.StatusMessage = $"렌더링 중... {progress:F1}%";
                            });
                        }
                    }
                    catch (Exception ex) { Debug.WriteLine($"[PROGRESS PARSE ERROR] {ex.Message}"); }
                }
            };
            progressViewModel.StatusMessage = "렌더링 시작...";
            process.Start();
            process.BeginErrorReadLine();
            await process.WaitForExitAsync(cancellationToken);
            if (cancellationToken.IsCancellationRequested) return false;
            if (process.ExitCode == 0)
            {
                progressViewModel.StatusMessage = "렌더링 완료!";
                progressViewModel.Progress = 100;
                return true;
            }
            else
            {
                progressViewModel.StatusMessage = $"오류: 렌더링에 실패했습니다. (종료 코드: {process.ExitCode})";
                return false;
            }
        }
    }
}