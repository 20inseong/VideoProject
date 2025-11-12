using System;

namespace VideoEditor.Models
{
    public class EmotionAnalysisResult
    {
        public string ClipTitle { get; set; }
        public double Timestamp { get; set; }
        public string Emotion { get; set; }
    }
}