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
                var whisperFactory = WhisperFactory.FromPath(_modelPath);

                using var processor = whisperFactory.CreateBuilder()
                    .WithLanguage("auto")
                    .WithProgressHandler(p => progress.Report(p))
                    .Build();

                FileStream? fileStream = null;
                const int maxRetries = 5;
                const int delayOnRetry = 300; // ms
                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        fileStream = new FileStream(audioPath, FileMode.Open, FileAccess.Read, FileShare.Read);
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
                                    await foreach (var result in processor.ProcessAsync(fileStream))
                                    {
                                        Console.WriteLine($"[Whisper Result] Start: {result.Start}, End: {result.End}, Text: {result.Text}");
                                        segments.Add(new TranscriptionSegment
                                        {
                                            Start = result.Start,
                                            End = result.End,
                                            Text = result.Text
                                        });
                                    }                }
            }
            finally
            {
                if (File.Exists(audioPath))
                {
                    try
                    {
                        File.Delete(audioPath);
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

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-i \"{videoPath}\" -vn -acodec pcm_s16le -ar 16000 -ac 1 \"{tempPath}\"",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                var error = await process.StandardError.ReadToEndAsync();
                Debug.WriteLine($"ffmpeg error: {error}");
                return null;
            }

            return tempPath;
        }
    }
}
