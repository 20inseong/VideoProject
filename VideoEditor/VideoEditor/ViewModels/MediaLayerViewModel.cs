using LibVLCSharp.Shared;
using System;
using VideoEditor.Common;
using VideoEditor.Models;

namespace VideoEditor.ViewModels
{
    public abstract class MediaLayerViewModel : ViewModelBase, IDisposable
    {
        public MediaPlayer MediaPlayer { get; }
        public TimelineClipBase SourceClip { get; }

        protected MediaLayerViewModel(LibVLC libvlc, TimelineClipBase sourceClip)
        {
            SourceClip = sourceClip;
            MediaPlayer = new MediaPlayer(libvlc) { EnableHardwareDecoding = true };
        }

        public void Sync(double timelinePosition, bool isPlaying)
        {
            double timeWithinClip = timelinePosition - SourceClip.StartPosition;

            double sourceStartTime = 0;
            if (SourceClip is VideoClip vc) sourceStartTime = vc.SourceStartTime;
            else if (SourceClip is AudioClip ac) sourceStartTime = ac.SourceStartTime;

            long seekTimeMs = (long)((sourceStartTime + timeWithinClip) * 1000);

            if (Math.Abs(MediaPlayer.Time - seekTimeMs) > 150)
            {
                MediaPlayer.Time = seekTimeMs;
            }

            if (isPlaying && !MediaPlayer.IsPlaying) MediaPlayer.Play();
            else if (!isPlaying && MediaPlayer.IsPlaying) MediaPlayer.Pause();
        }

        public void Dispose()
        {
            MediaPlayer?.Stop();
            MediaPlayer?.Dispose();
        }
    }
}