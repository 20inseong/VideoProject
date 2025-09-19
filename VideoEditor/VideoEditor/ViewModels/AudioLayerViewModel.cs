using LibVLCSharp.Shared;
using System;
using VideoEditor.Models;

namespace VideoEditor.ViewModels
{
    public class AudioLayerViewModel : MediaLayerViewModel
    {
        // base 생성자에서 isAudioOnly 파라미터 호출 제거
        public AudioLayerViewModel(LibVLC libvlc, AudioClip sourceClip) : base(libvlc, sourceClip)
        {
            var media = new Media(libvlc, new Uri(sourceClip.AudioPath));

            // ★★★ 바로 여기입니다! Media 객체에 옵션을 추가합니다. ★★★
            media.AddOption("--no-video");

            MediaPlayer.Media = media;
        }
    }
}