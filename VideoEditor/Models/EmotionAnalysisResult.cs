using System;

namespace VideoEditor.Models
{
    /// <summary>
    /// UI에 감정 분석 결과를 표시하기 위한 데이터 모델
    /// </summary>
    public class EmotionAnalysisResult
    {
        public string ClipTitle { get; set; }
        public double Timestamp { get; set; }
        public string Emotion { get; set; }
    }
}