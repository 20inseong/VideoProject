using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using VideoEditor.Models;
using Whisper.net;
using Whisper.net.Ggml;

namespace VideoEditor.Common
{
    public class SpeechToTextService
    {
        private readonly string _modelPath;

        public SpeechToTextService(string modelPath)
        {
            _modelPath = modelPath;
        }

        public async Task<List<TranscriptionSegment>> TranscribeAsync(string videoPath, IProgress<int> progress)
        {
            var audioPath = await ExtractAudioAsync(videoPath);
            if (audioPath == null)
            {
                return new List<TranscriptionSegment>();
            }

            var segments = new List<TranscriptionSegment>();

            try
            {
                // 오디오 파일이 생성되었는지 확인하고 잠시 대기
                if (!File.Exists(audioPath))
                {
                    Debug.WriteLine($"Audio file not found: {audioPath}");
                    return segments;
                }

                // 파일이 완전히 쓰여질 때까지 대기
                await Task.Delay(500);

                Debug.WriteLine($"Starting transcription for: {audioPath}, Size: {new FileInfo(audioPath).Length} bytes");

                var whisperFactory = WhisperFactory.FromPath(_modelPath);

                using var processor = whisperFactory.CreateBuilder()
                    .WithLanguage("auto")
                    .WithProgressHandler(p => 
                    {
                        Debug.WriteLine($"Transcription progress: {p}%");
                        progress.Report(p);
                    })
                    .Build();

                FileStream? fileStream = null;
                const int maxRetries = 10;
                const int delayOnRetry = 500; // ms
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        fileStream = new FileStream(audioPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                        Debug.WriteLine($"Successfully opened audio file on attempt {i + 1}");
                        break; 
                    }
                    catch (IOException ex)
                    {
                        Debug.WriteLine($"Attempt {i + 1} to open {audioPath} failed: {ex.Message}");
                        if (i == maxRetries - 1) throw; 
                        await Task.Delay(delayOnRetry);
                    }
                }
                
                if (fileStream == null) throw new InvalidOperationException("Could not open file stream.");

                using (fileStream) 
                {
                    Debug.WriteLine($"Starting Whisper ProcessAsync for stream of length {fileStream.Length}");
                    int segmentCount = 0;
                    
                    await foreach (var result in processor.ProcessAsync(fileStream))
                    {
                        segmentCount++;
                        Debug.WriteLine($"[Whisper Segment {segmentCount}] Start: {result.Start}, End: {result.End}, Text: {result.Text}");
                        segments.Add(new TranscriptionSegment
                        {
                            Start = result.Start,
                            End = result.End,
                            Text = result.Text
                        });
                    }
                    
                    Debug.WriteLine($"Transcription completed. Total segments: {segmentCount}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Transcription error: {ex.GetType().Name} - {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                throw;
            }
            finally
            {
                if (File.Exists(audioPath))
                {
                    try
                    {
                        // 파일 삭제 전에 잠시 대기하여 모든 핸들이 닫히도록 함
                        await Task.Delay(1000);
                        File.Delete(audioPath);
                        Debug.WriteLine($"Deleted temporary audio file: {audioPath}");
                    }
                    catch(Exception ex)
                    {
                        Debug.WriteLine($"Failed to delete temp file {audioPath}: {ex.Message}");
                    }
                }
            }

            return segments;
        }

        private async Task<string?> ExtractAudioAsync(string videoPath)
        {
            var tempPath = Path.GetTempFileName() + ".wav";
            var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "bin", "ffmpeg.exe");

            Debug.WriteLine($"Extracting audio from: {videoPath}");
            Debug.WriteLine($"Temporary audio path: {tempPath}");
            Debug.WriteLine($"FFmpeg path: {ffmpegPath}");

            if (!File.Exists(ffmpegPath))
            {
                Debug.WriteLine($"FFmpeg not found at: {ffmpegPath}");
                return null;
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                // 긴 영상을 위한 최적화된 FFmpeg 인자
                // -y: 파일 덮어쓰기 자동 승인
                // -i: 입력 파일
                // -vn: 비디오 스트림 무시
                // -acodec pcm_s16le: PCM 16비트 리틀 엔디안 오디오 코덱
                // -ar 16000: 16kHz 샘플링 레이트 (Whisper 최적화)
                // -ac 1: 모노 채널
                Arguments = $"-y -i \"{videoPath}\" -vn -acodec pcm_s16le -ar 16000 -ac 1 \"{tempPath}\"",
                RedirectStandardError = true,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = processStartInfo };
            
            var errorOutput = new System.Text.StringBuilder();
            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrEmpty(e.Data))
                {
                    errorOutput.AppendLine(e.Data);
                    Debug.WriteLine($"FFmpeg: {e.Data}");
                }
            };

            try
            {
                process.Start();
                process.BeginErrorReadLine();
                
                // 긴 영상을 위해 충분한 시간을 주되, 무한 대기는 방지
                // 30분까지 대기 (대부분의 비디오 처리에 충분)
                var timeoutTask = Task.Delay(TimeSpan.FromMinutes(30));
                var processTask = process.WaitForExitAsync();
                
                var completedTask = await Task.WhenAny(processTask, timeoutTask);
                
                if (completedTask == timeoutTask)
                {
                    Debug.WriteLine("FFmpeg process timed out after 30 minutes");
                    try
                    {
                        process.Kill();
                    }
                    catch { }
                    return null;
                }

                if (process.ExitCode != 0)
                {
                    Debug.WriteLine($"FFmpeg exited with code {process.ExitCode}");
                    Debug.WriteLine($"FFmpeg error output:\n{errorOutput}");
                    return null;
                }

                // 파일이 생성되었는지 확인
                if (!File.Exists(tempPath))
                {
                    Debug.WriteLine($"Audio file was not created at: {tempPath}");
                    return null;
                }

                var fileInfo = new FileInfo(tempPath);
                Debug.WriteLine($"Audio extraction completed. File size: {fileInfo.Length} bytes");
                
                if (fileInfo.Length == 0)
                {
                    Debug.WriteLine("Warning: Extracted audio file is empty");
                    return null;
                }

                return tempPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error extracting audio: {ex.Message}");
                Debug.WriteLine($"Stack trace: {ex.StackTrace}");
                return null;
            }
        }
    }
}
