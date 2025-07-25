using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using VideoEditor.Common;
using VideoEditor.ViewModels;
using VideoEditor.Models;

namespace VideoEditor.ViewModels
{
    public class MainViewModel :ViewModelBase
    {
        public PlayerViewModel PlayerViewModel { get; }
        public VideoListViewModel VideoList { get; }
        public MainViewModel()
        {
            PlayerViewModel = new PlayerViewModel();
            VideoList = new VideoListViewModel();

            VideoList.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(VideoList.SelectedVideoItem))
                {
                    if (VideoList.SelectedVideoItem != null)
                    {
                        PlayerViewModel.LoadMedia(VideoList.SelectedVideoItem.FullPath);
                        PlayerViewModel.MediaPlayer.Play();
                    }
                }
                else
                {
                    // 선택된 항목이 없을 경우 미디어 정지 및 초기화
                    PlayerViewModel.MediaPlayer.Stop();
                    if (PlayerViewModel.MediaPlayer.Media != null)
                    {
                        PlayerViewModel.MediaPlayer.Media.Dispose();
                        PlayerViewModel.MediaPlayer.Media = null;
                    }
                }
            };
        }
    }
}
