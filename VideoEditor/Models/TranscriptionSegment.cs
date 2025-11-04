
using System;

namespace VideoEditor.Models
{
    public class TranscriptionSegment
    {
        public TimeSpan Start { get; set; }
        public TimeSpan End { get; set; }
        public string Text { get; set; }

        public TimeSpan Duration => End - Start;
        public long DurationMs => (long)Duration.TotalMilliseconds;
    }
}
