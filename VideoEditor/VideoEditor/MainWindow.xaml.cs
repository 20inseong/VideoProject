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
        private ExportProgressWindow? _progressWindow;
        private Myvideo _draggedVideo = null;
        private Point _dragStartPoint;
        private Line _playheadLine;
        private double _currentTimelineDurationSec = 300;
        public MainWindow()
        {
            InitializeComponent();

            Common.UIDispatcher.Initialize();
            _mainViewModel = new MainViewModel(this);
            DataContext = _mainViewModel;

            _mainViewModel.ExportStarted += MainViewModel_ExportStarted;
            _mainViewModel.ExportFinished += MainViewModel_ExportFinished;

            videoView.MediaPlayer = _mainViewModel.PlayerViewModel.MainVideoPlayer;

            this.Loaded += MainWindow_Loaded;
            _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;

            _mainViewModel.VideoEditor.PropertyChanged += VideoEditor_PropertyChanged;

            _mainViewModel.PlayerViewModel.MainVideoPlayer.LengthChanged += MediaPlayer_LengthChanged;
            _mainViewModel.PlayerViewModel.MainVideoPlayer.Stopped += MediaPlayer_Stopped;

            TimelineScrollViewer.ScrollChanged += (s, e) =>
            {
                RulerScrollViewer.ScrollToHorizontalOffset(TimelineScrollViewer.HorizontalOffset);
            };

            DrawTimelineRuler();

            InitializeNewViewModel();
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            var newWindow = new MainWindow();

            newWindow.Show();

            //this.Close();
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (DataContext is MainViewModel vm)
            {
                vm.Dispose();
                System.Diagnostics.Debug.WriteLine("[Project] ViewModel resources have been disposed.");
            }
        }

        private void InitializeNewViewModel()
        {
            if (_mainViewModel != null)
            {
                _mainViewModel.ExportStarted -= MainViewModel_ExportStarted;
                _mainViewModel.ExportFinished -= MainViewModel_ExportFinished;
                _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
                if (_mainViewModel.VideoEditor != null)
                {
                    _mainViewModel.VideoEditor.PropertyChanged -= VideoEditor_PropertyChanged;
                }
            }

            _mainViewModel = new MainViewModel(this);
            DataContext = _mainViewModel;

            _mainViewModel.ExportStarted += MainViewModel_ExportStarted;
            _mainViewModel.ExportFinished += MainViewModel_ExportFinished;
            _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
            _mainViewModel.VideoEditor.PropertyChanged += VideoEditor_PropertyChanged;

            videoView.MediaPlayer = _mainViewModel.PlayerViewModel.MainVideoPlayer;
            DrawTimelineRuler();
            System.Diagnostics.Debug.WriteLine("[Project] A new ViewModel has been initialized.");
        }

        private void Project_Reset_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("현재 프로젝트의 초기화를 시작하시겠습니까? 저장하지 않은 내용은 사라집니다.",
                                         "새 프로젝트",
                                         MessageBoxButton.YesNo,
                                         MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                InitializeNewViewModel();
            }
        }


        private void VideoEditor_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(VideoEditorViewModel.PixelsPerSecond))
            {
                Dispatcher.Invoke(() => DrawTimelineRuler());
            }
        }

        private void MainViewModel_ExportStarted(object? sender, ExportStartedEventArgs e)
        {
            _progressWindow = new ExportProgressWindow
            {
                DataContext = e.ProgressViewModel,
                Owner = this
            };

            this.IsEnabled = false;
            _progressWindow.Show();
        }

        private void MainViewModel_ExportFinished(object? sender, EventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                if (_progressWindow != null)
                {
                    _progressWindow.AllowClose();

                    _progressWindow.Close();
                    _progressWindow = null;
                }

                this.IsEnabled = true;
                this.Activate();
            });
        }

        private void btnSelectMedia_Click(object sender, RoutedEventArgs e)
        {
            OpenFileDialog openFileDialog = new OpenFileDialog();

            string videoFilter = "*.mp4;*.avi;*.mkv;*.mov;*.wmv";
            string audioFilter = "*.mp3;*.wav;*.m4a;*.aac";
            string imageFilter = "*.jpg;*.jpeg;*.png;*.bmp";

            string allSupportedFilter = $"{videoFilter};{audioFilter};{imageFilter}";

            openFileDialog.Filter =
                $"모든 미디어 파일 ({allSupportedFilter})|{allSupportedFilter}|" +
                $"비디오 파일 ({videoFilter})|{videoFilter}|" +
                $"오디오 파일 ({audioFilter})|{audioFilter}|" +
                $"이미지 파일 ({imageFilter})|{imageFilter}|" +
                "모든 파일 (*.*)|*.*";

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
            if (e.Data.GetDataPresent("TimelineClip"))
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

        private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(MainViewModel.TotalTimelineDurationMs))
            {
                Dispatcher.Invoke(() =>
                {
                    _currentTimelineDurationSec = _mainViewModel.TotalTimelineDurationMs / 1000.0;
                    DrawTimelineRuler();

                    System.Diagnostics.Debug.WriteLine($"[UI Event] Ruler updated to: {_currentTimelineDurationSec:F2} seconds.");
                });
            }
            if (e.PropertyName == nameof(MainViewModel.CurrentTimelinePosition))
            {
                Dispatcher.Invoke(() =>
                {
                    if (_playheadLine != null)
                    {
                        double newX = _mainViewModel.CurrentTimelinePosition * _mainViewModel.VideoEditor.PixelsPerSecond;

                        _playheadLine.X1 = newX;
                        _playheadLine.X2 = newX;
                    }
                });
            }
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

        private void ApplySpeedButton_Click(object sender, RoutedEventArgs e)
        {
            if (float.TryParse(SpeedTextBox.Text, out float speed))
            {
                speed = Math.Max(0.1f, Math.Min(25.0f, speed));
                _mainViewModel.PlayerViewModel.PlaybackRate = speed;
                SpeedTextBox.Text = speed.ToString("F2");
            }
            else
            {
                MessageBox.Show("올바른 배속 값을 입력해주세요. (0.1 ~ 25.0)", "배속 설정 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                SpeedTextBox.Text = _mainViewModel.PlayerViewModel.PlaybackRate.ToString("F2");
            }
        }

        private void SpeedMenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (SpeedControlPanel.Visibility == Visibility.Visible)
            {
                SpeedControlPanel.Visibility = Visibility.Collapsed;
                VideoInfoPanel.Visibility = Visibility.Visible;
                VideoInfoPanel.Margin = new Thickness(10, 40, 10, 10);
            }
            else
            {
                SpeedControlPanel.Visibility = Visibility.Visible;
                VideoInfoPanel.Visibility = Visibility.Collapsed;
                SpeedControlPanel.Margin = new Thickness(10, 40, 10, 10);
            }
        }

    }
}