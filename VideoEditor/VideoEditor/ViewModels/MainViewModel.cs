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
			// 첫 클립 추가 시에만 마스터 플레이어를 로드하고 음소거하여 타임라인 클록으로 사용
			if (PlayerViewModel.Layers.Count == 0)
			{
				PlayerViewModel.LoadMedia(e.VideoPath, disableVideoOutput: true);
				PlayerViewModel.MediaPlayer.Volume = 0; // 마스터는 음소거 (중복 오디오 방지)
				PlayerViewModel.MediaPlayer.Play();
			}

			// 모든 클립은 가시 레이어로 추가되어 비디오/오디오가 재생됨
			var z = PlayerViewModel.Layers.Count; // 다음 ZIndex
			var layer = PlayerViewModel.AddLayer(e.VideoPath, left: 0, top: 0, width: 640, height: 360, opacity: 1.0, zIndex: z);
			// 첫 레이어 추가 직후 명시적으로 재생 보장
			layer.MediaPlayer.Time = PlayerViewModel.MediaPlayer.Time;
			layer.MediaPlayer.Play();

            // StatusMessage = $"'{System.IO.Path.GetFileNameWithoutExtension(e.VideoPath)}' 클립 재생을 시작합니다.";
        }
    }
}
