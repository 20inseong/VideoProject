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

        private readonly string _ffmpegPath;

        public FFmpegExportService()
        {
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var candidate1 = Path.Combine(baseDir, "ffmpeg", "bin", "ffmpeg.exe");
            var candidate2 = Path.Combine(baseDir, "ffmpeg.exe");
            _ffmpegPath = File.Exists(candidate1) ? candidate1 : candidate2;
        }

        public async Task<bool> ExportVideoAsync(
            ICollection<TimelineClipBase> clips,
            double totalDurationSeconds,
            string outputPath,
            ExportProgressViewModel progressViewModel,
            CancellationToken cancellationToken,
            double previewWidth = 800.0,
            double previewHeight = 450.0)
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
                var (inputArguments, filterComplex) = await BuildFFmpegArguments(clips, totalDurationSeconds, tempDirectory, cancellationToken, previewWidth, previewHeight);

                var tempScriptPath = Path.Combine(tempDirectory, $"filter_script_{DateTime.UtcNow:yyyyMMdd_HHmmss_fff}.txt");
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

                            CancellationToken cancellationToken,

                            double previewWidth,

                            double previewHeight)

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

            // 텍스트 클립 수집 (ASS로 렌더링)
            var textClips = clips.OfType<TextClip>().ToList();

            foreach (var clip in clips)
            {
                string clipId = clip.Id.ToString("N");
                switch (clip)
                {
                    case VideoClip vc:
                        {
                            int fileIndex = inputFiles.IndexOf(vc.VideoPath);
                            double sourceDuration = vc.Duration * vc.SpeedRatio;

                            double scaleX = OutputWidth / previewWidth;
                            double scaleY = OutputHeight / previewHeight;

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

                            double scaleX = OutputWidth / previewWidth;
                            double scaleY = OutputHeight / previewHeight;

                            double targetWidth, targetHeight;
                                                        
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
                            double sourceDuration = audioClip.Duration * audioClip.SpeedRatio;

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

            double positionScaleX = OutputWidth / previewWidth;
            double positionScaleY = OutputHeight / previewHeight;

            // 텍스트 클립을 ASS로 작성하여 단일 subtitles 필터로 처리 (성능 개선)
            string? assFilePath = null;
            if (textClips.Count > 0)
            {
                var sbAss = new StringBuilder();
                sbAss.AppendLine("[Script Info]");
                sbAss.AppendLine("ScriptType: v4.00+");
                sbAss.AppendLine("PlayResX: 1920");
                sbAss.AppendLine("PlayResY: 1080");
                sbAss.AppendLine("ScaledBorderAndShadow: yes");
                sbAss.AppendLine();
                sbAss.AppendLine("[V4+ Styles]");
                sbAss.AppendLine("Format: Name, Fontname, Fontsize, PrimaryColour, SecondaryColour, OutlineColour, BackColour, Bold, Italic, Underline, StrikeOut, ScaleX, ScaleY, Spacing, Angle, BorderStyle, Outline, Shadow, Alignment, MarginL, MarginR, MarginV, Encoding");
                // 기본 스타일: 가운데 정렬(5), 굵게, 흰색, 얇은 외곽선
                sbAss.AppendLine("Style: Default,Malgun Gothic,48,&H00FFFFFF,&H00FFFFFF,&H00000000,&H80000000,-1,0,0,0,100,100,0,0,1,2,0,5,10,10,10,1");
                sbAss.AppendLine();
                sbAss.AppendLine("[Events]");
                sbAss.AppendLine("Format: Layer, Start, End, Style, Name, MarginL, MarginR, MarginV, Effect, Text");

                // 텍스트 클립 그대로 사용 (병합 없음)
                var ordered = textClips.OrderBy(t => t.StartPosition).ToList();

                foreach (var m in ordered)
                {
                    // 최소 표시 시간 1.0s
                    if (m.Duration < 1.0) m.Duration = 1.0;

                    // 위치: 텍스트 박스 중앙
                    double posX = (m.X + m.RenderWidth / 2.0) * positionScaleX;
                    double posY = (m.Y + m.RenderHeight / 2.0) * positionScaleY;

                    // 라인 래핑: 줄당 32자, 최대 2줄
                    string text = m.Text?.Trim() ?? string.Empty;
                    if (text.Length > 64)
                    {
                        text = text.Substring(0, 64);
                    }
                    text = WrapText(text, 32, 2);

                    // 색상/투명도/폰트 크기
                    var color = m.ForegroundColor;
                    double op = m.Opacity > 1.0 ? m.Opacity / 100.0 : m.Opacity;
                    op = Math.Clamp(op, 0.0, 1.0);
                    byte a = (byte)Math.Clamp((int)Math.Round((1.0 - op) * 255.0), 0, 255);
                    // 자막 태그: 색상은 &HBBGGRR, 알파는 &HAA&
                    string colorBgr = $"&H{color.B:X2}{color.G:X2}{color.R:X2}";
                    string alphaTag = $"&H{a:X2}&";
                    int fontSize = (int)Math.Max(10, Math.Round(m.FontSize * positionScaleY));
                    int align = 5; // Middle Center
                    int ml = 10, mr = 10, mv = 10;


                    // 폰트 패밀리 오버라이드 (ASS \\fn 사용). 한글 폰트명 매핑
                    string fontName = string.IsNullOrWhiteSpace(m.FontFamily) ? "Malgun Gothic" : m.FontFamily;
                    if (fontName == "맑은 고딕") fontName = "Malgun Gothic";
                    fontName = fontName.Replace("{", string.Empty).Replace("}", string.Empty);

                    string start = ToAssTime(m.StartPosition);
                    string end = ToAssTime(m.StartPosition + m.Duration);

                    // 이벤트 텍스트: 스타일 오버라이드 (회전 보정: 부호 반전, 폰트 적용)
                    string ov = $"{{\\pos({posX:F2},{posY:F2})\\fn{fontName}\\fs{fontSize}\\c{colorBgr}\\alpha{alphaTag}\\an{align}" + (Math.Abs(m.Rotation) > 0.01 ? $"\\frz{-m.Rotation:F1}" : "") + "}";
                    string safe = EscapeAssText(text);
                    sbAss.AppendLine($"Dialogue: 0,{start},{end},Default,,{ml},{mr},{mv},,{ov}{safe}");
                }

                assFilePath = Path.Combine(tempDirectory, "subs.ass");
                await File.WriteAllTextAsync(assFilePath, sbAss.ToString(), cancellationToken);
            }

            // 한글 폰트(맑은 고딕)를 사용하도록 수정합니다.
            string fontPath = "C:/Windows/Fonts/malgun.ttf".Replace(":", "\\:");

            // Z-order 관리: 비디오 클립 → (이미지 + 텍스트 혼합) 순서
            // 비디오는 항상 맨 아래, 이미지와 텍스트는 TrackIndex에 따라 함께 정렬
            // TrackIndex 오름차순 정렬: Track 0 → 1 → 2 → 3 → 4 순서로 오버레이
            // 나중에 오버레이될수록 위에 표시되므로 Track 0이 맨 아래, Track 4가 맨 위에 표시됨
            
            // 1. 비디오 클립들 (TrackIndex 오름차순 - 낮은 번호부터)
            var videoClips = clips
                .OfType<VideoClip>()
                .Where(c => clipIdToProcessedStreamMap.ContainsKey(c.Id))
                .OrderBy(c => c.TrackIndex)
                .ToList();

            // 2. 이미지와 텍스트 클립들을 함께 정렬 (TrackIndex 오름차순 - 낮은 번호부터)
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
                    
                    lastVideoStream = nextVideoStream;
                    overlayCounter++;
                }
                else if (clip is TextClip textClip)
                {
                    // 텍스트 클립은 ASS 자막으로 일괄 처리하므로 여기서는 건너뜁니다.
                    continue;
                }
            }
            // 최종 비디오
            if (!string.IsNullOrEmpty(assFilePath))
            {
                // 단일 subtitles 필터로 자막 전체 적용 (Windows 경로 이스케이프 및 fontsdir 명시)
                string nextVideoStream = $"[final_v_{overlayCounter}]";
                var assEscaped = assFilePath.Replace("\\", "/").Replace(":", "\\:");
                var fontsDir = "C\\:/Windows/Fonts";
                filterComplex.AppendLine($"{lastVideoStream}subtitles=filename='{assEscaped}':fontsdir='{fontsDir}'{nextVideoStream};");
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
                RedirectStandardOutput = false,
                UseShellExecute = false,
                CreateNoWindow = true,
                StandardErrorEncoding = Encoding.UTF8,
                WorkingDirectory = Path.GetDirectoryName(_ffmpegPath) ?? AppDomain.CurrentDomain.BaseDirectory
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

        private async Task<(string imagePath, double offsetX, double offsetY)> RenderTextToImage(TextClip textClip, string tempDirectory, int counter, double scaleX, double scaleY)
        {
            try
            {
                string imagePath = Path.Combine(tempDirectory, $"text_{counter}.png");
                double offsetX = 0;
                double offsetY = 0;

                Console.WriteLine($"\n[EXPORT TEXT] Rendering TextClip '{textClip.Text}'");
                Console.WriteLine($"  - Original: X={textClip.X:F2}, Y={textClip.Y:F2}, W={textClip.RenderWidth:F2}, H={textClip.RenderHeight:F2}");
                Console.WriteLine($"  - Scale: X={scaleX:F4}, Y={scaleY:F4}");
                Console.WriteLine($"  - Rotation: {textClip.Rotation:F2}°");

                await UIDispatcher.InvokeAsync(() =>
                {
                    // 텍스트 렌더링을 위한 크기 계산 (스케일 적용)
                    double renderWidth = textClip.RenderWidth * scaleX;
                    double renderHeight = textClip.RenderHeight * scaleY;
                    double fontSize = textClip.FontSize * scaleY;
                    
                    Console.WriteLine($"  - Scaled: W={renderWidth:F2}, H={renderHeight:F2}, FontSize={fontSize:F2}");
                    
                    // 패딩 적용 (OverlayWindow와 동일하게)
                    double paddingX = 10 * scaleX;
                    double paddingY = 5 * scaleY;

                    var typeface = new Typeface(new FontFamily(textClip.FontFamily), FontStyles.Normal, FontWeights.Bold, FontStretches.Normal);
                    var textBrush = new SolidColorBrush(textClip.ForegroundColor);
                    textBrush.Freeze();

                    // 먼저 FormattedText를 생성하여 실제 텍스트 크기를 측정
                    var dpiInfo = VisualTreeHelper.GetDpi(Application.Current.MainWindow);
                    var formattedText = new FormattedText(
                        textClip.Text,
                        CultureInfo.CurrentCulture,
                        FlowDirection.LeftToRight,
                        typeface,
                        fontSize,
                        textBrush,
                        dpiInfo.PixelsPerDip)
                    {
                        TextAlignment = TextAlignment.Left,
                        Trimming = TextTrimming.None
                    };

                    // 실제 텍스트 크기 (패딩 포함) - WidthIncludingTrailingWhitespace 사용
                    double actualTextWidth = formattedText.WidthIncludingTrailingWhitespace + (paddingX * 2);
                    double actualTextHeight = formattedText.Height + (paddingY * 2);
                    
                    // 최소 크기 보장
                    actualTextWidth = Math.Max(actualTextWidth, 50);
                    actualTextHeight = Math.Max(actualTextHeight, 30);

                    // RenderWidth x RenderHeight 영역 내에서 텍스트가 중앙에 위치할 때의 오프셋
                    offsetX = (renderWidth - actualTextWidth) / 2.0;
                    offsetY = (renderHeight - actualTextHeight) / 2.0;

                    Console.WriteLine($"  - Actual text size: W={actualTextWidth:F2}, H={actualTextHeight:F2}");
                    Console.WriteLine($"  - Offset in box: X={offsetX:F2}, Y={offsetY:F2}");

                    // 회전을 고려한 캔버스 크기 계산
                    double canvasWidth = actualTextWidth;
                    double canvasHeight = actualTextHeight;
                    
                    // 회전이 있는 경우 캔버스 크기를 대각선 길이로 확장
                    if (Math.Abs(textClip.Rotation) > 0.01)
                    {
                        // 대각선 길이 계산 (회전해도 잘리지 않도록)
                        double diagonal = Math.Sqrt(actualTextWidth * actualTextWidth + actualTextHeight * actualTextHeight);
                        canvasWidth = diagonal;
                        canvasHeight = diagonal;
                        
                        // 오프셋도 조정 (확장된 캔버스에 맞춰)
                        double widthDiff = canvasWidth - actualTextWidth;
                        double heightDiff = canvasHeight - actualTextHeight;
                        offsetX -= widthDiff / 2.0;
                        offsetY -= heightDiff / 2.0;
                    }

                    // DrawingVisual을 사용하여 텍스트 렌더링
                    var drawingVisual = new DrawingVisual();
                    using (DrawingContext dc = drawingVisual.RenderOpen())
                    {
                        // 투명 배경
                        dc.DrawRectangle(Brushes.Transparent, null, new Rect(0, 0, canvasWidth, canvasHeight));

                        // 회전 처리
                        if (Math.Abs(textClip.Rotation) > 0.01)
                        {
                            double centerX = canvasWidth / 2.0;
                            double centerY = canvasHeight / 2.0;
                            
                            dc.PushTransform(new TranslateTransform(centerX, centerY));

                            dc.PushTransform(new RotateTransform(textClip.Rotation));
                            dc.PushOpacity(textClip.Opacity);
                            
                            // 텍스트를 중앙에 배치
                            double textX = -formattedText.WidthIncludingTrailingWhitespace / 2.0;
                            double textY = -formattedText.Height / 2.0;
                            dc.DrawText(formattedText, new Point(textX, textY));
                            
                            dc.Pop(); // Opacity
                            dc.Pop(); // Rotation
                            dc.Pop(); // Translation
                        }
                        else
                        {
                            // 회전 없음 - 캔버스 중앙에 텍스트 배치
                            dc.PushOpacity(textClip.Opacity);
                            
                            // 캔버스 중앙에 텍스트 배치
                            double textX = (canvasWidth - formattedText.WidthIncludingTrailingWhitespace) / 2.0;
                            double textY = (canvasHeight - formattedText.Height) / 2.0;
                            dc.DrawText(formattedText, new Point(textX, textY));
                            
                            dc.Pop(); // Opacity
                        }
                    }

                    // RenderTargetBitmap으로 비트맵 생성
                    var renderBitmap = new RenderTargetBitmap(
                       (int)Math.Ceiling(canvasWidth),
                       (int)Math.Ceiling(canvasHeight),
                       96, 96,
                       PixelFormats.Pbgra32);
                    renderBitmap.Render(drawingVisual);

                    Console.WriteLine($"  - Image size: {renderBitmap.PixelWidth}x{renderBitmap.PixelHeight}");
                    Console.WriteLine($"  - Saved to: {imagePath}");

                    // PNG로 저장
                    using (var fileStream = new FileStream(imagePath, FileMode.Create))
                    {
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(renderBitmap));
                        encoder.Save(fileStream);
                    }
                });

                return (imagePath, offsetX, offsetY);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EXPORT] Failed to render text to image: {ex.Message}");
                Console.WriteLine($"[EXPORT] Stack trace: {ex.StackTrace}");
                return (null, 0, 0);
            }
        }

        private static string WrapText(string input, int maxPerLine, int maxLines)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var words = input.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var lines = new List<string>();
            var curr = new StringBuilder();
            foreach (var w in words)
            {
                if (curr.Length + (curr.Length > 0 ? 1 : 0) + w.Length > maxPerLine)
                {
                    lines.Add(curr.ToString());
                    curr.Clear();
                    if (lines.Count >= maxLines) break;
                }
                if (curr.Length > 0) curr.Append(' ');
                curr.Append(w);
            }
            if (lines.Count < maxLines && curr.Length > 0) lines.Add(curr.ToString());
            return string.Join("\\N", lines);
        }

        private static string ToAssTime(double seconds)
        {
            if (seconds < 0) seconds = 0;
            var ts = TimeSpan.FromSeconds(seconds);
            return string.Format(CultureInfo.InvariantCulture, "{0}:{1:00}:{2:00}.{3:00}", (int)ts.TotalHours, ts.Minutes, ts.Seconds, ts.Milliseconds / 10);
        }

        private static string EscapeAssText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            const string NL = "\uE000";   // placeholder for \N/\n
            const string NBSP = "\uE001"; // placeholder for \h
            // Preserve ASS control sequences first
            text = text.Replace("\\N", NL).Replace("\\n", NL).Replace("\\h", NBSP);
            // Remove a dangling backslash at line end or right before a newline placeholder
            if (text.EndsWith("\\")) text = text.Substring(0, text.Length - 1);
            text = text.Replace("\\" + NL, NL);

            // Escape other special chars
            text = text.Replace("\\", "\\\\").Replace("{", "\\{").Replace("}", "\\}");
            // Restore preserved sequences
            text = text.Replace(NL, "\\N").Replace(NBSP, "\\h");
            return text;
        }
    }
}