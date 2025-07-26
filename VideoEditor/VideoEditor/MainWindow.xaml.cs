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
using System.Windows.Shapes;

namespace VideoEditor
{
    public partial class MainWindow : Window
    {
        private MainViewModel _mainViewModel;
        private Myvideo _draggedVideo = null;
        private Point _dragStartPoint;
        public MainWindow()
        {
            InitializeComponent();

            _mainViewModel = new MainViewModel();
            DataContext = _mainViewModel;

            videoView.MediaPlayer = _mainViewModel.PlayerViewModel.MediaPlayer;

            TimelineScrollViewer.ScrollChanged += (s, e) =>
            {
                RulerScrollViewer.ScrollToHorizontalOffset(TimelineScrollViewer.HorizontalOffset);
            };

            ThumbnailItemsControl.DataContext = _mainViewModel.VideoEditor;
            ClipsItemsControl.DataContext = _mainViewModel.VideoEditor;

            DrawTimelineRuler();
        }

        private void btnSelectMedia_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();
            openFileDialog.Filter = "Media files (*.mp4;*.avi;*.mkv;*.mov)|*.mp4;*.avi;*.mkv;*.mov|All files (*.*)|*.*";

            if (openFileDialog.ShowDialog() == true)
            {
                string videoPath = openFileDialog.FileName;
                string videoTitle = System.IO.Path.GetFileNameWithoutExtension(videoPath);

                Myvideo newVideo = new Myvideo
                {
                    Title = videoTitle,
                    FullPath = videoPath,
                    Category = "사용자 추가"
                };

                _mainViewModel.VideoList.AddVideo(newVideo);
                _mainViewModel.VideoList.SelectedVideoItem = newVideo;

                StatusTextBlock.Text = $"미디어가 목록에 추가되었습니다.";
            }
        }

        private void Timeline_Drop(object sender, DragEventArgs e) 
        {
            if (e.Data.GetDataPresent("Myvideo"))
            {
                Myvideo droppedVideo = e.Data.GetData("Myvideo") as Myvideo;
                if (droppedVideo != null)
                {
                    Point dropPosition = e.GetPosition(TimelineClipsCanvas);

                    _mainViewModel.VideoEditor.AddVideoClip(droppedVideo, dropPosition.X);

                    _mainViewModel.PlayerViewModel.LoadMedia(droppedVideo.FullPath);
                    _mainViewModel.PlayerViewModel.MediaPlayer.Play();

                             // 상태 표시 업데이트
                    StatusTextBlock.Text = $" 타임라인에 추가되었습니다. 재생을 시작합니다.";

                }
            }
            e.Handled = true; // 이벤트 처리 완료
        }

        private void Timeline_DragOver(object sender, DragEventArgs e) 
        {
            // 드래그되는 데이터가 Myvideo 타입인지 확인
            if (e.Data.GetDataPresent("Myvideo"))
            {
                e.Effects = DragDropEffects.Copy; // 복사 효과 표시
            }
            else
            {
                e.Effects = DragDropEffects.None; // 드롭 불가
            }
            e.Handled = true; // 이벤트 처리 완료
        }
        private void VideoList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) 
        {
            _dragStartPoint = e.GetPosition(null); // 마우스 클릭 시작 지점 저장
            ListBox parent = (ListBox)sender;
            _draggedVideo = parent.SelectedItem as Myvideo; // 드래그할 Myvideo 객체 저장
        }
        private void VideoList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e) 
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedVideo != null)
            {
                Point currentPosition = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPosition;

                // 마우스가 일정 거리 이상 이동했을 때만 드래그 시작
                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    // 드래그 데이터 생성 (Myvideo 객체)
                    DataObject dragData = new DataObject("Myvideo", _draggedVideo);
                    DragDrop.DoDragDrop(mideaListBox, dragData, DragDropEffects.Copy);

                    _draggedVideo = null; // 드래그 시작 후 초기화
                }
            }
        }

        // 타임라인 눈금자 그리기 (예시, 실제로는 더 정교한 로직 필요)
        private void DrawTimelineRuler()
        {
            TimelineRulerCanvas.Children.Clear(); // 기존 눈금자 지우기

            double totalWidth = TimelineClipsCanvas.Width; // 타임라인 캔버스의 현재 너비
            double pixelsPerSecond = _mainViewModel.VideoEditor.PixelsPerSecond; // 1초당 픽셀 수

            // 1초 단위로 눈금 그리기
            for (double i = 0; i < totalWidth; i += pixelsPerSecond)
            {
                Line line = new Line
                {
                    X1 = i,
                    Y1 = 0,
                    X2 = i,
                    Y2 = 20, // 짧은 눈금 길이
                    Stroke = Brushes.LightGray,
                    StrokeThickness = 1
                };
                TimelineRulerCanvas.Children.Add(line);

                // 5초 단위로 긴 눈금 및 시간 텍스트
                if ((i / pixelsPerSecond) % 5 == 0)
                {
                    line.Y2 = 30; // 긴 눈금 길이
                    TextBlock textBlock = new TextBlock
                    {
                        Text = TimeSpan.FromSeconds(i / pixelsPerSecond).ToString(@"mm\:ss"),
                        Foreground = Brushes.White,
                        Margin = new Thickness(i + 5, 35, 0, 0) // 눈금 옆에 시간 표시
                    };
                    TimelineRulerCanvas.Children.Add(textBlock);
                }
            }
        }

        // 타임라인 캔버스 크기 변경 시 눈금자 다시 그리기 (선택 사항)
        private void TimelineClipsCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawTimelineRuler();
        }
    }
}