using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoEditor.Common;
using VideoEditor.ViewModels;
using VideoEditor.Models;
//roxnook233 push test
namespace VideoEditor.ViewModels
{
    public class MainViewModel :ViewModelBase
    {
        public PlayerViewModel PlayerViewModel { get; }
        public VideoListViewModel VideoList { get; }
        public VideoEditorViewModel VideoEditor { get; }
        private string _statusMessage;

        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        public MainViewModel()
        {
            PlayerViewModel = new PlayerViewModel();
            VideoList = new VideoListViewModel();
            VideoEditor = new VideoEditorViewModel();

            VideoEditor.OnClipAdded += MainViewModel_OnClipAdded;
        }

        private void MainViewModel_OnClipAdded(object? sender, ClipAddedEventArgs e)
        {
            // 모든 클립은 가시 레이어로 추가되어 비디오/오디오가 재생됨 (소프트 클록 사용)
            var z = PlayerViewModel.Layers.Count; // 다음 ZIndex
            var layer = PlayerViewModel.AddLayer(e.VideoPath, left: 0, top: 0, width: 640, height: 360, opacity: 1.0, zIndex: z);
            // 현재 클록 시간으로 동기화
            layer.MediaPlayer.Time = PlayerViewModel.CurrentTime;
            layer.MediaPlayer.Play();

            // 클립과 레이어 연결
            var addedClip = VideoEditor.TimelineClips.LastOrDefault();
            if (addedClip != null)
            {
                addedClip.AssociatedLayer = layer;
            }

            // StatusMessage = $"'{System.IO.Path.GetFileNameWithoutExtension(e.VideoPath)}' 클립 재생을 시작합니다.";
        }
    }
}
