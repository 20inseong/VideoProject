using LibVLCSharp.Shared;
using VideoEditor.Common;
using VideoEditor.Models;

namespace VideoEditor.ViewModels
{
    public class VideoLayerViewModel : MediaLayerViewModel
    {
        public bool IsVisible => true;
        public double Left => 0;
        public double Top => 0;
        public double Width { get; set; } = 1920;
        public double Height { get; set; } = 1080;
        public int ZIndex { get; }

        public VideoLayerViewModel(LibVLC libvlc, VideoClip sourceClip) : base(libvlc, sourceClip)
        {
            //ZIndex = sourceClip.TrackIndex;
            //var media = new Media(libvlc, new Uri(sourceClip.VideoPath));
            //MediaPlayer.Media = media;

            const int MaxTracks = 5;
            ZIndex = MaxTracks - sourceClip.TrackIndex;

            var media = new Media(libvlc, new Uri(sourceClip.VideoPath));
            MediaPlayer.Media = media;
        }
    }
}