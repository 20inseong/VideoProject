using System.Collections.Generic;
using System.Threading.Tasks;
using VideoEditor.Models; // 1단계에서 만든 모델을 사용합니다.

namespace VideoEditor.Common
{
    /// <summary>
    /// UI 테스트를 위한 가짜(Mock) 분석 데이터 제공 서비스입니다.
    /// 실제 EmotionDetect 클래스를 대체하여 사용합니다.
    /// </summary>
    public class EmotionDetectTestDataService
    {
        // 생성자가 필요 없습니다.
        // public EmotionDetectTestDataService(string modelPath) { }

        /// <summary>
        /// 미리 정의된 가짜 분석 결과 리스트를 즉시 반환합니다.
        /// (실제 분석 메서드와 이름 및 반환 형식을 맞추는 것이 좋습니다.)
        /// </summary>
        public Task<List<EmotionAnalysisResult>> AnalyzeVideoEmotionAsync(string clipTitle, double videoDuration, int fps, int frameInterval)
        {
            var mockData = new List<EmotionAnalysisResult>
            {
                // --- "휴가 영상" 클립 데이터 ---
                // (clipTitle 파라미터를 사용해서 실제 클립 제목을 넣어줄 수 있습니다)
                new EmotionAnalysisResult { ClipTitle = clipTitle, Timestamp = 0.0, Emotion = "neutral" },
                new EmotionAnalysisResult { ClipTitle = clipTitle, Timestamp = 4.0, Emotion = "happy" },
                new EmotionAnalysisResult { ClipTitle = clipTitle, Timestamp = 8.0, Emotion = "surprise" },
                new EmotionAnalysisResult { ClipTitle = clipTitle, Timestamp = 12.0, Emotion = "happy" },

                // --- 다른 클립 예시 ---
                new EmotionAnalysisResult { ClipTitle = "다른 클립.mp4", Timestamp = 0.0, Emotion = "happy" },
                new EmotionAnalysisResult { ClipTitle = "다른 클립.mp4", Timestamp = 3.0, Emotion = "surprise" },

                // --- 또 다른 클립 예시 ---
                new EmotionAnalysisResult { ClipTitle = "공포 예고편.mov", Timestamp = 5.0, Emotion = "fear" },
                new EmotionAnalysisResult { ClipTitle = "공포 예고편.mov", Timestamp = 10.0, Emotion = "angry" },
            };

            // 비동기(async) 메서드를 흉내 내기 위해 Task.FromResult를 사용합니다.
            return Task.FromResult(mockData);
        }
    }
}