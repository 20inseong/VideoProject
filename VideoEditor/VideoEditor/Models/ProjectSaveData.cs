using System.Collections.Generic;

namespace VideoEditor.Models
{
    public class ProjectSaveData
    {
        public List<TimelineClipBase> TimelineClips { get; set; }
        public List<Myvideo> MediaBin { get; set; }

        public ProjectSaveData()
        {
            TimelineClips = new List<TimelineClipBase>();
            MediaBin = new List<Myvideo>();
        }
    }
}