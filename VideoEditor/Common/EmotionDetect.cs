using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using VideoEditor.Models;

namespace VideoEditor.Common
{
    public class EmotionDetect
    {
        private readonly string TempFrameFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "TempFramesDebug");
        private readonly string PythonExe;
        private readonly string PythonScriptPath;

        public EmotionDetect(string pythonExe, string pythonScriptPath)
        {
            PythonExe = pythonExe;
            PythonScriptPath = pythonScriptPath;
        }

        public async Task<string?> ExtractFrameAsync(string videoPath, double timestampSeconds)
        {
            var tempPath = Path.Combine(TempFrameFolder, $"{timestampSeconds:F2}.png");
            var ffmpegPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg", "bin", "ffmpeg.exe");

            var psi = new ProcessStartInfo
            {
                FileName = ffmpegPath,
                Arguments = $"-ss {timestampSeconds} -i \"{videoPath}\" -frames:v 1 -y \"{tempPath}\"", // -y 추가 (덮어쓰기 허용)
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = Process.Start(psi);
            if (process == null) return null;

            string error = await process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync();

            if (process.ExitCode != 0 && !error.Contains("already exists. Overwrite?"))
            {
                Debug.WriteLine($"ffmpeg error: {error}");
                return null;
            }
            return tempPath;
        }

        public async Task<List<EmotionAnalysisResult>> RunPythonEmotionDetectionAsync(string clipTitle, IProgress<int> progress)
        {
            var psi = new ProcessStartInfo
            {
                FileName = PythonExe,
                Arguments = $"\"{PythonScriptPath}\" \"{TempFrameFolder}\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };

            using var process = new Process { StartInfo = psi };

            process.OutputDataReceived += (sender, e) =>
            {
                if (string.IsNullOrWhiteSpace(e.Data)) return;

                // 디버그 창에는 모든 표준 출력을 기록
                Debug.WriteLine("[PYTHON STDOUT]: " + e.Data);

                // "Progress:"로 시작하는 문자열인지 확인
                if (e.Data.StartsWith("Progress:"))
                {
                    // "Progress:"와 "%"를 제거하고 숫자 부분만 추출
                    var progressString = e.Data.Replace("Progress:", "").Trim();
                    if (double.TryParse(progressString, NumberStyles.Any, CultureInfo.InvariantCulture, out double progressValue))
                    {
                        // IProgress<T>를 통해 진행률을 MainViewModel로 보고
                        progress?.Report((int)progressValue);
                    }
                }
            };

            process.ErrorDataReceived += (sender, e) =>
            {
                if (!string.IsNullOrWhiteSpace(e.Data))
                {
                    Debug.WriteLine("[PYTHON STDERR]: " + e.Data);
                }
            };

            process.Start();

            process.BeginOutputReadLine();
            process.BeginErrorReadLine();

            await process.WaitForExitAsync();

            var jsonPath = Path.Combine(TempFrameFolder, "result.json");

            if (!File.Exists(jsonPath))
            {
                Debug.WriteLine("Error: result.json not found.");
                return null;
            }

            string json = await File.ReadAllTextAsync(jsonPath);
            var mockData = JsonSerializer.Deserialize<List<EmotionAnalysisResult>>(json);
            if (mockData == null)
            {
                return new List<EmotionAnalysisResult>();
            }
            foreach (var item in mockData)
            {
                item.ClipTitle = clipTitle;
            }

            try
            {
                await Task.Delay(100);
                if (Directory.Exists(TempFrameFolder))
                {
                    Directory.Delete(TempFrameFolder, true);
                    Debug.WriteLine("Temporary files deleted.");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Cleanup failed: " + ex.Message);
            }

            return mockData;
        }
    }
}