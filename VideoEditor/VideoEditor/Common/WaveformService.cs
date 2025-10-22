
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;

namespace VideoEditor.Common
{
    public class WaveformService
    {
        public async Task<List<Point>> GenerateWaveformDataAsync(string mediaPath, int resolution = 1000)
        {
            var audioPath = await ExtractAudioAsync(mediaPath);
            if (string.IsNullOrEmpty(audioPath) || !File.Exists(audioPath))
            {
                return new List<Point>();
            }

            var waveformData = new List<Point>();
            try
            {
                byte[] wavBytes = await File.ReadAllBytesAsync(audioPath);
                // Standard 44-byte WAV header for PCM
                const int headerSize = 44; 
                if (wavBytes.Length <= headerSize) return waveformData;

                int totalSamples = (wavBytes.Length - headerSize) / 2; // 16-bit samples
                int samplesPerPoint = totalSamples / resolution;
                if (samplesPerPoint == 0) samplesPerPoint = 1;

                for (int i = 0; i < resolution; i++)
                {
                    short min = 0, max = 0;
                    int currentBaseIndex = headerSize + (i * samplesPerPoint * 2);

                    for (int j = 0; j < samplesPerPoint; j++)
                    {
                        int sampleIndex = currentBaseIndex + (j * 2);
                        if (sampleIndex + 1 >= wavBytes.Length) break;

                        short sample = BitConverter.ToInt16(wavBytes, sampleIndex);
                        if (sample < min) min = sample;
                        if (sample > max) max = sample;
                    }

                    waveformData.Add(new Point(min / 32768.0, max / 32768.0));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error generating waveform data: {ex.Message}");
            }
            finally
            {
                if (File.Exists(audioPath))
                {
                    try
                    {
                        File.Delete(audioPath);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Failed to delete temp audio file {audioPath}: {ex.Message}");
                    }
                }
            }

            return waveformData;
        }

        private async Task<string?> ExtractAudioAsync(string videoPath)
        {
            var tempPath = Path.GetTempFileName() + ".wav";
            var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "bin", "ffmpeg.exe");

            if (!File.Exists(ffmpegPath))
            {
                Debug.WriteLine("ffmpeg.exe not found!");
                return null;
            }
             if (!File.Exists(videoPath))
            {
                Debug.WriteLine($"Media file not found: {videoPath}");
                return null;
            }

            var processStartInfo = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-i \"{videoPath}\" -vn -acodec pcm_s16le -ar 16000 -ac 1 \"{tempPath}\" -y",
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = processStartInfo };
            process.Start();
            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0)
            {
                Debug.WriteLine($"ffmpeg error: {error}");
                return null;
            }

            return tempPath;
        }
    }
}
