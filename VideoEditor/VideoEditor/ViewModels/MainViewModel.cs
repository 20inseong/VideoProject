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
            // 크기를 지정하지 않으면 해상도에 맞게 자동 설정됨
            var layer = PlayerViewModel.AddLayer(e.VideoPath, left: 0, top: 0, opacity: 1.0, zIndex: z);
            // 현재 클록 시간으로 동기화
            layer.MediaPlayer.Time = PlayerViewModel.CurrentTime;
            layer.MediaPlayer.Play();

            // StatusMessage = $"'{System.IO.Path.GetFileNameWithoutExtension(e.VideoPath)}' 클립 재생을 시작합니다.";
        }
    }
}
