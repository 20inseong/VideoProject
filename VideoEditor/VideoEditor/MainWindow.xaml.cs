 using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using Microsoft.Win32;
using VideoEditor.Models;
using VideoEditor.ViewModels;
using System.IO;

namespace VideoEditor
{
    public partial class MainWindow : Window
    {
        private MainViewModel _mainViewModel;
        public MainWindow()
        {
            InitializeComponent();

            _mainViewModel = new MainViewModel();
            DataContext = _mainViewModel;

            videoView.MediaPlayer = _mainViewModel.PlayerViewModel.MediaPlayer;
        }

        private void btnSelectMedia_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Media files (*.mp4;*.avi;*.mkv;*.mov)|*.mp4;*.avi;*.mkv;*.mov|All files (*.*)|*.*";

            if (openFileDialog.ShowDialog() == true)
            {
                string videoPath = openFileDialog.FileName;
                string videoTitle = Path.GetFileNameWithoutExtension(videoPath); // 파일명에서 확장자 제거

                // VideoItem 인스턴스 생성
                Myvideo newVideo = new Myvideo
                {
                    Title = videoTitle,
                    FullPath = videoPath,
                    Category = "사용자 추가"
                };

                // MainViewModel을 통해 VideoList에 비디오 추가
                _mainViewModel.VideoList.AddVideo(newVideo);

                // 파일을 선택하면 자동으로 재생 목록에서 선택되도록 설정 (옵션)
                _mainViewModel.VideoList.SelectedVideoItem = newVideo;
            }
        }

        //private void VideoList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        //{
        //    if (sender is ListBox listBox && listBox.SelectedItem is Myvideo selectedMyVideo)
        //    {
        //        _mainViewModel.PlayerViewModel.LoadMedia(selectedMyVideo.FullPath);
        //    }
        //}

        // 드래그앤드롭 관련 함수들은 현재 고려하지 않으므로 비워두거나 삭제해도 됩니다.
        private void Timeline_Drop(object sender, DragEventArgs e) { }
        private void Timeline_DragOver(object sender, DragEventArgs e) { }
        private void VideoList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) { }
        private void VideoList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e) { }
    }
}