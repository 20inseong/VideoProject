using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
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
                var (inputArguments, filterComplex) = await BuildFFmpegArguments(clips, totalDurationSeconds, tempDirectory, cancellationToken);

                var tempScriptPath = Path.Combine(tempDirectory, "filter_script.txt");
                await File.WriteAllTextAsync(tempScriptPath, filterComplex, cancellationToken);

                string encoder = DetectHardwareEncoder();
                string videoCodecArguments;

                switch (encoder)
                {
                    case "h264_nvenc":
                        videoCodecArguments = "-c:v h264_nvenc -preset p4 -cq 23";
                        break;
                    case "h264_qsv":
                        videoCodecArguments = "-c:v h264_qsv -preset medium -global_quality 23";
                        break;
                    case "h264_amf":
                        videoCodecArguments = "-c:v h264_amf -rc cqp -qp_i 23 -qp_p 23 -qp_b 23";
                        break;
                    default: // libx264
                        videoCodecArguments = "-c:v libx264 -preset medium -crf 23";
                        break;
                }

                var arguments = new StringBuilder();
                arguments.Append(inputArguments);
                arguments.Append($"-filter_complex_script \"{tempScriptPath}\" ");
                arguments.Append("-map \"[final_v]\" -map \"[final_a]\" ");
                arguments.Append($"{videoCodecArguments} -c:a aac -b:a 192k -y \"{tempOutputPath}\"");

                Console.WriteLine("--- FFmpeg DEBUG START ---");
                Console.WriteLine("Final FFmpeg Command:");
                Console.WriteLine($"{_ffmpegPath} {arguments}");
                Console.WriteLine("\nFilter Script Content:");
                Console.WriteLine(filterComplex);
                Console.WriteLine("--- FFmpeg DEBUG END ---");

                bool success = await RunFFmpegProcess(arguments.ToString(), totalDurationSeconds, progressViewModel, cancellationToken);

                if (success) { File.Move(tempOutputPath, outputPath, true); }
                return success;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXPORT CRITICAL ERROR] {ex.Message}");
                progressViewModel.StatusMessage = $"치명적인 오류 발생: {ex.Message}";
                return false;
            }
            finally
            {
                try { if (Directory.Exists(tempDirectory)) { Directory.Delete(tempDirectory, true); } }
                catch (Exception ex) { Console.WriteLine($"[EXPORT] Failed to clean up temp directory: {tempDirectory}. Reason: {ex.Message}"); }
            }
        }

                        private async Task<(string inputArguments, string filterComplex)> BuildFFmpegArguments(

                            ICollection<TimelineClipBase> clips,

                            double totalDurationSeconds,

                            string tempDirectory,

                            CancellationToken cancellationToken)

                        {

                

                            Console.WriteLine("\n--- BEGIN TIMELINE CLIP DATA DUMP (Passed to FFmpeg Service) ---");

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

                                Console.WriteLine(sb.ToString());

                            }

                            Console.WriteLine("--- END TIMELINE CLIP DATA DUMP ---\n");

                

                            var inputFiles = clips.Select(c => c switch

                            {

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
            
            // 회전으로 인한 크기 변화를 추적 (위치 보정에 사용)
            var rotatedSizeOffsets = new Dictionary<Guid, (double offsetX, double offsetY)>();

            foreach (var clip in clips)
            {
                string clipId = clip.Id.ToString("N");
                switch (clip)
                {
                    case VideoClip vc:
                        {
                            int fileIndex = inputFiles.IndexOf(vc.VideoPath);
                            double sourceDuration = vc.Duration * vc.SpeedRatio;

                            double scaleX = OutputWidth / PreviewWidth;
                            double scaleY = OutputHeight / PreviewHeight;

                            double targetWidth = vc.RenderWidth * scaleX;
                            double targetHeight = vc.RenderHeight * scaleY;

                            string resizeFilter = $"scale={targetWidth.ToString("F0", culture)}:{targetHeight.ToString("F0", culture)}";

                            string speedAndTrimFilter = $"trim=start={vc.SourceStartTime.ToString("F6", culture)}:duration={sourceDuration.ToString("F6", culture)}, " +
                            $"setpts=PTS-STARTPTS, " +
                            $"setpts=PTS/({vc.SpeedRatio.ToString("F6", culture)}), " +
                            $"setpts=PTS+({vc.StartPosition.ToString("F6", culture)})/TB, ";

                            filterComplex.AppendLine($"[{fileIndex}:v] {speedAndTrimFilter}{resizeFilter}, setsar=1 [processed_{clipId}];");
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

                            double targetWidth, targetHeight;
                            
                            // CustomWidth/Height는 원본 픽셀 단위의 절대값
                            // RenderWidth/Height는 미리보기에서의 표시 크기
                            // 미리보기와 동일한 비율로 출력하려면:
                            // 1. RenderWidth/Height를 출력 해상도로 스케일 (기본)
                            // 2. CustomWidth/Height가 설정되어 있으면, 그 비율을 적용
                            
                            if (ic.CustomWidth > 0 && ic.CustomHeight > 0 && ic.InitialRenderWidth > 0 && ic.InitialRenderHeight > 0)
                            {
                                // CustomWidth/Height가 SourceWidth/Height 대비 몇 배인지 계산
                                double widthRatio = ic.CustomWidth / ic.SourceWidth;
                                double heightRatio = ic.CustomHeight / ic.SourceHeight;
                                
                                // InitialRenderWidth/Height에 비율을 적용한 후 출력 해상도로 스케일
                                targetWidth = ic.InitialRenderWidth * widthRatio * scaleX;
                                targetHeight = ic.InitialRenderHeight * heightRatio * scaleY;
                            }
                            else
                            {
                                // CustomWidth/Height가 설정되지 않았으면 현재 RenderWidth/Height 사용
                                targetWidth = ic.RenderWidth * scaleX;
                                targetHeight = ic.RenderHeight * scaleY;
                            }

                            // 회전 시 위치 오프셋 계산
                            double offsetX = 0;
                            double offsetY = 0;
                            
                            if (Math.Abs(ic.Rotation) > 0.01)
                            {
                                // 회전 후 크기: hypot(w, h) = sqrt(w^2 + h^2)
                                double rotatedSize = Math.Sqrt(targetWidth * targetWidth + targetHeight * targetHeight);
                                
                                // 회전으로 인한 크기 증가분
                                double widthIncrease = rotatedSize - targetWidth;
                                double heightIncrease = rotatedSize - targetHeight;
                                
                                // 중심을 유지하기 위해 offset 조정 (크기 증가의 절반만큼 뒤로 이동)
                                offsetX = -widthIncrease / 2.0;
                                offsetY = -heightIncrease / 2.0;
                                
                                rotatedSizeOffsets[ic.Id] = (offsetX, offsetY);
                            }

                            // 필터 빌드 순서:
                            // 1. 투명도 먼저 적용 (원본 이미지에)
                            // 2. 크기 조절
                            // 3. 회전 (투명 배경 사용)
                            var filterParts = new List<string>();
                            
                            // 투명도 필터를 가장 먼저 적용 (100%가 아닌 경우에만)
                            if (Math.Abs(ic.Opacity - 100.0) > 0.01)
                            {
                                double alphaValue = ic.Opacity / 100.0;
                                string alphaFilter = $"colorchannelmixer=aa={alphaValue.ToString("F2", culture)}";
                                filterParts.Add(alphaFilter);
                            }
                            
                            // 크기 조절
                            string resizeFilter = $"scale={targetWidth.ToString("F0", culture)}:{targetHeight.ToString("F0", culture)}";
                            filterParts.Add(resizeFilter);
                            
                            // 회전 필터 추가 (0도가 아닌 경우에만)
                            if (Math.Abs(ic.Rotation) > 0.01)
                            {
                                // 회전 각도를 라디안으로 변환
                                double rotationRadians = ic.Rotation * Math.PI / 180.0;
                                // 투명 배경 사용 (0x00000000 = 완전 투명)
                                // fillcolor 옵션으로 투명 배경 설정
                                string rotateFilter = $"rotate={rotationRadians.ToString("F6", culture)}:c=none:ow='hypot(iw,ih)':oh='hypot(iw,ih)'";
                                filterParts.Add(rotateFilter);
                            }
                            
                            string videoStreamFilter = $"loop=loop=-1:size=1,trim=duration={totalDurationSeconds.ToString("F6", culture)},setpts=PTS-STARTPTS";
                            
                            string combinedFilters = videoStreamFilter + "," + string.Join(",", filterParts) + ", setsar=1";
                            
                            filterComplex.AppendLine($"[{fileIndex}:v] {combinedFilters} [processed_{clipId}];");
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

            string lastVideoStream = "[base_v]";
            int overlayCounter = 0;

            double positionScaleX = OutputWidth / PreviewWidth;
            double positionScaleY = OutputHeight / PreviewHeight;

            // 한글 폰트(맑은 고딕)를 사용하도록 수정합니다.
            string fontPath = "C:/Windows/Fonts/malgun.ttf".Replace(":", "\\:");

            // Z-order 관리: 비디오 클립 → (이미지 + 텍스트 혼합) 순서
            // 비디오는 항상 맨 아래, 이미지와 텍스트는 TrackIndex에 따라 함께 정렬
            // TrackIndex 오름차순 정렬: Track 4 → 3 → 2 → 1 → 0 순서로 오버레이
            // 나중에 오버레이될수록 위에 표시되므로 Track 0이 맨 위에 표시됨
            
            // 1. 비디오 클립들 (TrackIndex 오름차순)
            var videoClips = clips
                .OfType<VideoClip>()
                .Where(c => clipIdToProcessedStreamMap.ContainsKey(c.Id))
                .OrderBy(c => c.TrackIndex)
                .ToList();

            // 2. 이미지와 텍스트 클립들을 함께 정렬 (TrackIndex 오름차순)
            var overlayClips = clips
                .Where(c => (c is ImageClip || c is TextClip) && clipIdToProcessedStreamMap.ContainsKey(c.Id))
                .OrderBy(c => c.TrackIndex)
                .ToList();

            // 1단계: 비디오 클립들 먼저 오버레이 (맨 아래)
            foreach (var videoClip in videoClips)
            {
                string nextVideoStream = $"[final_v_{overlayCounter}]";
                string streamToOverlay = clipIdToProcessedStreamMap[videoClip.Id];
                double targetX = videoClip.X * positionScaleX;
                double targetY = videoClip.Y * positionScaleY;
                string ffmpegX = targetX.ToString("F2", culture);
                string ffmpegY = targetY.ToString("F2", culture);
                string enableOption = $"enable='between(t,{videoClip.StartPosition.ToString("F6", culture)},{(videoClip.StartPosition + videoClip.Duration).ToString("F6", culture)})'";
                filterComplex.AppendLine($"{lastVideoStream}{streamToOverlay} overlay=x={ffmpegX}:y={ffmpegY}:{enableOption} {nextVideoStream};");
                
                lastVideoStream = nextVideoStream;
                overlayCounter++;
            }

            // 2단계: 이미지와 텍스트 클립들을 TrackIndex 순서대로 오버레이
            int textFileCounter = 0;
            foreach (var clip in overlayClips)
            {
                string nextVideoStream = $"[final_v_{overlayCounter}]";

                if (clip is ImageClip imageClip)
                {
                    string streamToOverlay = clipIdToProcessedStreamMap[imageClip.Id];
                    double targetX = imageClip.X * positionScaleX;
                    double targetY = imageClip.Y * positionScaleY;
                    
                    // 회전으로 인한 오프셋 적용
                    if (rotatedSizeOffsets.TryGetValue(imageClip.Id, out var offset))
                    {
                        targetX += offset.offsetX;
                        targetY += offset.offsetY;
                    }
                    
                    string ffmpegX = targetX.ToString("F2", culture);
                    string ffmpegY = targetY.ToString("F2", culture);
                    string enableOption = $"enable='between(t,{imageClip.StartPosition.ToString("F6", culture)},{(imageClip.StartPosition + imageClip.Duration).ToString("F6", culture)})'";
                    filterComplex.AppendLine($"{lastVideoStream}{streamToOverlay} overlay=x={ffmpegX}:y={ffmpegY}:{enableOption} {nextVideoStream};");
                }
                else if (clip is TextClip textClip)
                {
                    double targetX = textClip.X * positionScaleX;
                    double targetY = textClip.Y * positionScaleY;
                    
                    string ffmpegX_text = targetX.ToString("F2", culture);
                    string ffmpegY_text = targetY.ToString("F2", culture);

                    // 사용자가 설정한 폰트 크기 사용 (기본값: 24)
                    double finalFontSize = textClip.FontSize * positionScaleY;

                    string enable_text = $"enable='between(t,{textClip.StartPosition.ToString("F6", culture)},{(textClip.StartPosition + textClip.Duration).ToString("F6", culture)})'";
                    
                    // 텍스트를 이미지로 렌더링하여 오버레이
                    string textImagePath = await RenderTextToImage(textClip, tempDirectory, textFileCounter, positionScaleX, positionScaleY);
                    
                    if (!string.IsNullOrEmpty(textImagePath))
                    {
                        // 이미지를 입력 파일로 추가
                        int textImageIndex = inputFiles.Count;
                        inputFiles.Add(textImagePath);
                        inputArguments.Append($"-i \"{textImagePath}\" ");

                        // 이미지를 비디오 스트림으로 변환하고 오버레이
                        string textStreamId = $"text_img_{textFileCounter}";
                        filterComplex.AppendLine($"[{textImageIndex}:v] loop=loop=-1:size=1,trim=duration={totalDurationSeconds.ToString("F6", culture)},setpts=PTS-STARTPTS [{textStreamId}];");
                        filterComplex.AppendLine($"{lastVideoStream}[{textStreamId}] overlay=x={ffmpegX_text}:y={ffmpegY_text}:{enable_text} {nextVideoStream};");
                        
                        lastVideoStream = nextVideoStream;
                        overlayCounter++;
                    }
                    
                    textFileCounter++;
                }
                
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

        private string DetectHardwareEncoder()
        {
            Console.WriteLine("--- Hardware Encoder Detection ---");
            string[] encodersToTest = { "h264_nvenc", "h264_amf", "h264_qsv" };
            string testArgs = "-f lavfi -i nullsrc=s=640x480 -t 1 -c:v {0} -f null -";

            foreach (var encoder in encodersToTest)
            {
                Console.WriteLine($"Testing encoder: {encoder}...");
                var pStartInfo = new ProcessStartInfo
                {
                    FileName = _ffmpegPath,
                    Arguments = string.Format(testArgs, encoder),
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = new Process { StartInfo = pStartInfo };
                process.Start();
                process.WaitForExit();

                if (process.ExitCode == 0)
                {
                    Console.WriteLine($"SUCCESS: Found working hardware encoder: {encoder}");
                    Console.WriteLine("--------------------------------");
                    return encoder;
                }
                else
                {
                    Console.WriteLine($"FAIL: Encoder {encoder} not available.");
                }
            }

            Console.WriteLine("No working hardware encoder found. Falling back to software encoder (libx264).");
            Console.WriteLine("--------------------------------");
            return "libx264";
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
                Console.WriteLine($"[FFMPEG LOG]: {e.Data}");
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
                    catch (Exception ex) { Console.WriteLine($"[PROGRESS PARSE ERROR] {ex.Message}"); }
                }
            };
            progressViewModel.StatusMessage = "렌더링 시작...";
            process.Start();
            process.BeginErrorReadLine();

            try
            {
                await process.WaitForExitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    Console.WriteLine("[EXPORT] FFmpeg process killed due to cancellation.");
                }
                progressViewModel.StatusMessage = "내보내기가 사용자에 의해 취소되었습니다.";
                return false;
            }
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

        private async Task<string> RenderTextToImage(TextClip textClip, string tempDirectory, int counter, double scaleX, double scaleY)
        {
            try
            {
                string imagePath = Path.Combine(tempDirectory, $"text_{counter}.png");

                await UIDispatcher.InvokeAsync(() =>
                {
                    // 텍스트 렌더링을 위한 크기 계산
                    double renderWidth = textClip.RenderWidth * scaleX;
                    double renderHeight = textClip.RenderHeight * scaleY;
                    double fontSize = textClip.FontSize * scaleY;

                    // DrawingVisual을 사용하여 텍스트 렌더링
                    var drawingVisual = new DrawingVisual();
                    using (DrawingContext drawingContext = drawingVisual.RenderOpen())
                    {
                        // 배경 (검은색 반투명 박스)
                        var backgroundBrush = new SolidColorBrush(Color.FromArgb(128, 0, 0, 0));
                        var backgroundPen = new Pen(backgroundBrush, 10);
                        drawingContext.DrawRectangle(backgroundBrush, backgroundPen, new Rect(0, 0, renderWidth, renderHeight));

                        // 텍스트
                        var formattedText = new FormattedText(
                            textClip.Text,
                            CultureInfo.CurrentCulture,
                            FlowDirection.LeftToRight,
                            new Typeface("Malgun Gothic"),
                            fontSize,
                            Brushes.White,
                            VisualTreeHelper.GetDpi(drawingVisual).PixelsPerDip)
                        {
                            MaxTextWidth = renderWidth,
                            MaxTextHeight = renderHeight,
                            TextAlignment = TextAlignment.Center,
                            Trimming = TextTrimming.None
                        };

                        // 텍스트를 중앙에 배치
                        double textX = 0;
                        double textY = (renderHeight - formattedText.Height) / 2;
                        drawingContext.DrawText(formattedText, new Point(textX, textY));
                    }

                    // RenderTargetBitmap으로 비트맵 생성
                    var renderBitmap = new RenderTargetBitmap(
                        (int)Math.Ceiling(renderWidth),
                        (int)Math.Ceiling(renderHeight),
                        96, 96,
                        PixelFormats.Pbgra32);
                    renderBitmap.Render(drawingVisual);

                    // PNG로 저장
                    using (var fileStream = new FileStream(imagePath, FileMode.Create))
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
                        encoder.Save(fileStream);
                    }
                });

                return imagePath;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXPORT] Failed to render text to image: {ex.Message}");
                return null;
            }
        }
    }
}