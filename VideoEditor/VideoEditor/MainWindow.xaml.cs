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
using VideoEditor.Common;
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

            Common.UIDispatcher.Initialize();
            _mainViewModel = new MainViewModel();
            DataContext = _mainViewModel;

            videoView.MediaPlayer = _mainViewModel.PlayerViewModel.MediaPlayer;

            this.Loaded += MainWindow_Loaded;
            _mainViewModel.PlayerViewModel.MediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
            _mainViewModel.PlayerViewModel.MediaPlayer.LengthChanged += MediaPlayer_LengthChanged;
            _mainViewModel.PlayerViewModel.MediaPlayer.Stopped += MediaPlayer_Stopped;

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
            openFileDialog.Multiselect = true;

            if (openFileDialog.ShowDialog() == true)
            {
                foreach (string videoPath in openFileDialog.FileNames)
                {
                    string videoTitle = System.IO.Path.GetFileNameWithoutExtension(videoPath);

                    Myvideo newVideo = new Myvideo
                    {
                        Title = videoTitle,
                        FullPath = videoPath,
                        Category = "사용자 추가"
                    };

                    _mainViewModel.VideoList.AddVideo(newVideo);
                }

                if (openFileDialog.FileNames.Any())
                {
                    _mainViewModel.VideoList.SelectedVideoItem = _mainViewModel.VideoList.MyVideoes.LastOrDefault();
                    StatusTextBlock.Text = $"{openFileDialog.FileNames.Length}개의 미디어가 목록에 추가되었습니다.";
                }
            }
        }

        private void Timeline_DragOver(object sender, DragEventArgs e) 
        {
            if (e.Data.GetDataPresent("VideoClip"))
            {
                e.Effects = DragDropEffects.Move;
            }
            else if (e.Data.GetDataPresent("Myvideo"))
            {
                e.Effects = DragDropEffects.Copy;
            }
            else
            {
                e.Effects = DragDropEffects.None;
            }
            e.Handled = true;
        }

        private void VideoList_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) 
        {
            _dragStartPoint = e.GetPosition(null);
            if (e.OriginalSource is DependencyObject source)
            {
                var listBoxItem = source.FindAncestor<ListBoxItem>();
                if (listBoxItem != null)
                {
                    _draggedVideo = listBoxItem.DataContext as Myvideo;
                }
            }
        }

        private void VideoList_PreviewMouseMove(object sender, System.Windows.Input.MouseEventArgs e) 
        {
            if (e.LeftButton == MouseButtonState.Pressed && _draggedVideo != null)
            {
                Point currentPosition = e.GetPosition(null);
                Vector diff = _dragStartPoint - currentPosition;

                if (Math.Abs(diff.X) > SystemParameters.MinimumHorizontalDragDistance ||
                    Math.Abs(diff.Y) > SystemParameters.MinimumVerticalDragDistance)
                {
                    DataObject dragData = new DataObject("Myvideo", _draggedVideo);
                    DragDrop.DoDragDrop(mideaListBox, dragData, DragDropEffects.Copy);

                    _draggedVideo = null;
                }
            }
        }

        private void DrawTimelineRuler()
        {
            if (TimelineRulerCanvas == null) return;

            TimelineRulerCanvas.Children.Clear();

            double pixelsPerSecond = _mainViewModel.VideoEditor.PixelsPerSecond;
            double totalDuration = _currentTimelineDurationSec;
            double totalWidth = totalDuration * pixelsPerSecond;

            TimelineRulerCanvas.Width = totalWidth;
            TimelineCanvas.Width = totalWidth;

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
                if (_playheadLine == null || !_mainViewModel.IsTimelinePlaying) return;

                var currentClip = _mainViewModel.VideoEditor.CurrentlyPlayingClip;
                if (currentClip != null)
                {
                    double clipStartTime = currentClip.StartPosition;
                    double timeWithinClip = e.Time / 1000.0;
                    double timelineCurrentTime = clipStartTime + timeWithinClip;

                    double newX = timelineCurrentTime * _mainViewModel.VideoEditor.PixelsPerSecond;

                    _playheadLine.X1 = newX;
                    _playheadLine.X2 = newX;
                }
            });
        }

        private void MediaPlayer_Stopped(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_mainViewModel.IsTimelinePlaying == false)
                {
                    _playheadLine.X1 = 0;
                    _playheadLine.X2 = 0;
                }
            });
        }
        private void PlayheadCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.Source is Canvas canvas)
            {
                Point position = e.GetPosition(canvas);
                double clickedTimeSec = position.X / _mainViewModel.VideoEditor.PixelsPerSecond;

                _mainViewModel.SeekTimeline(clickedTimeSec);

                _playheadLine.X1 = position.X;
                _playheadLine.X2 = position.X;
            }
        }

    }
}