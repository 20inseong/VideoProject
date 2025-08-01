using System.ComponentModel;
using System.IO;
 using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using Microsoft.Win32;
using VideoEditor.Models;
using VideoEditor.ViewModels;

namespace VideoEditor
{
    public partial class MainWindow : Window
    {
        private MainViewModel _mainViewModel;
        private Myvideo _draggedVideo = null;
        private Point _dragStartPoint;
        private Line _playheadLine;
        private double _currentTimelineDurationSec = 300;
        public MainWindow()
        {
            InitializeComponent();

            _mainViewModel = new MainViewModel();
            DataContext = _mainViewModel;

            videoView.MediaPlayer = _mainViewModel.PlayerViewModel.MediaPlayer;

            // 창이 완전히 로드된 후 초기화 작업을 수행하도록 이벤트를 연결합니다.
            this.Loaded += MainWindow_Loaded;
            // MediaPlayer의 시간이 바뀔 때마다 플레이헤드를 업데이트하도록 이벤트를 연결합니다.
            _mainViewModel.PlayerViewModel.MediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
            // MediaPlayer의 전체 길이가 바뀔 때마다 눈금자를 업데이트하도록 이벤트를 연결합니다.
            _mainViewModel.PlayerViewModel.MediaPlayer.LengthChanged += MediaPlayer_LengthChanged;

            TimelineScrollViewer.ScrollChanged += (s, e) =>
            {
                RulerScrollViewer.ScrollToHorizontalOffset(TimelineScrollViewer.HorizontalOffset);
            };

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

                //StatusTextBlock.Text = $"미디어가 목록에 추가되었습니다.";
            }
        }

        private async void Timeline_Drop(object sender, DragEventArgs e) 
        {
            if (e.Data.GetDataPresent("Myvideo"))
            {
                Myvideo droppedVideo = e.Data.GetData("Myvideo") as Myvideo;
                if (droppedVideo == null || !System.IO.File.Exists(droppedVideo.FullPath)) return;

                try
                    {
                    Point dropPosition = e.GetPosition(TimelineClipsCanvas);
                    double startTimeInSeconds = dropPosition.X / _mainViewModel.VideoEditor.PixelsPerSecond;

                    await _mainViewModel.VideoEditor.AddVideoClip(droppedVideo, startTimeInSeconds);

                    StatusTextBlock.Text = $"'{droppedVideo.Title}' 클립이 타임라인에 추가되었습니다.";

                    _mainViewModel.PlayerViewModel.LoadMedia(droppedVideo.FullPath);
                    _mainViewModel.PlayerViewModel.MediaPlayer.Play();
                    }
                    catch (Exception ex)
                    {
                        MessageBox.Show($"클립 추가 중 오류 발생: {ex.Message}", "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                    e.Handled = true; // 이벤트 처리 완료
            }
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

        private void DrawTimelineRuler()
        {
            if (TimelineRulerCanvas == null || ThumbnailItemsControl == null) return;

            TimelineRulerCanvas.Children.Clear();

            double pixelsPerSecond = _mainViewModel.VideoEditor.PixelsPerSecond;
            double totalDuration = _currentTimelineDurationSec; // VideoEditor의 변수 이름 사용
            double totalWidth = totalDuration * pixelsPerSecond;

            TimelineRulerCanvas.Width = totalWidth;

            if (ThumbnailItemsControl != null)
            {
                ThumbnailItemsControl.Width = totalWidth;
            }

            // 1초마다 얇은 선, 5초마다 굵은 선+숫자
            for (int sec = 0; sec <= totalDuration; sec++)
            {
                double x = sec * pixelsPerSecond;
                bool isMajorTick = sec % 5 == 0;

                var line = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 0,
                    Y2 = isMajorTick ? 30 : 10,
                    Stroke = isMajorTick ? Brushes.LightGray : Brushes.Gray,
                    StrokeThickness = isMajorTick ? 2 : 1
                };

                TimelineRulerCanvas.Children.Add(line);

                // 5초마다 숫자 표시
                if (isMajorTick)
                {
                    var text = new TextBlock
                    {
                        Text = TimeSpan.FromSeconds(sec).ToString(@"m\:ss"),
                        Foreground = Brushes.White,
                        FontSize = 12
                    };
                    Canvas.SetLeft(text, x + 2);
                    Canvas.SetTop(text, 10);
                    TimelineRulerCanvas.Children.Add(text);
                }
            }
        }
        //private void TimelineClipsCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        //{
        //    DrawTimelineRuler();
        //}

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            InitializePlayhead();
            DrawTimelineRuler();
        }

        private void InitializePlayhead()
        {
            _playheadLine = new Line
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Y1 = 0,
                Y2 = PlayheadCanvas.ActualHeight > 0 ? PlayheadCanvas.ActualHeight : 300
            };
            PlayheadCanvas.Children.Add(_playheadLine);
        }

        private void MediaPlayer_LengthChanged(object sender, LibVLCSharp.Shared.MediaPlayerLengthChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                double videoDurationSec = e.Length / 1000.0;

                _currentTimelineDurationSec = Math.Max(300.0, videoDurationSec);

                DrawTimelineRuler();
            });
        }

        private void MediaPlayer_TimeChanged(object sender, LibVLCSharp.Shared.MediaPlayerTimeChangedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_playheadLine == null) return;

                double currentSeconds = e.Time / 1000.0;

                double newX = currentSeconds * _mainViewModel.VideoEditor.PixelsPerSecond;

                _playheadLine.X1 = newX;
                _playheadLine.X2 = newX;
            });
        }

        //private void PlayerViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        //{
        //    if (e.PropertyName == nameof(PlayerViewModel.TotalDuration))
        //    {
        //        Dispatcher.Invoke(() =>
        //        {
        //            long totalMilliseconds = _mainViewModel.PlayerViewModel.TotalDuration;

        //            double totalSeconds = totalMilliseconds / 1000.0;

        //            if (totalSeconds > 0)
        //            {
        //                _currentVideoLengthSec = totalSeconds;
        //                DrawTimelineRuler();
        //            }
        //        });
        //    }
        //}
    }
}