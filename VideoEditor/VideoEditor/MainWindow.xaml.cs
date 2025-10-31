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
using System.Windows.Threading;

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
        private DateTime _lastDragUpdateTime = DateTime.MinValue;
        private const int DRAG_UPDATE_THROTTLE_MS = 50; // 50ms 간격으로 업데이트
        private DispatcherTimer _rulerRedrawTimer;


        public MainWindow()
        {
            InitializeComponent();

            Common.UIDispatcher.Initialize();
            _mainViewModel = new MainViewModel(this);
            DataContext = _mainViewModel;

            _mainViewModel.ExportStarted += MainViewModel_ExportStarted;
            _mainViewModel.ExportFinished += MainViewModel_ExportFinished;

            InitializeVideoViews();

            this.Loaded += MainWindow_Loaded;
            _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;

            _mainViewModel.VideoEditor.PropertyChanged += VideoEditor_PropertyChanged;

            _rulerRedrawTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _rulerRedrawTimer.Tick += (s, e) =>
            {
                _rulerRedrawTimer.Stop();
                DrawTimelineRuler();
            };

            TimelineScrollViewer.ScrollChanged += (s, e) =>
            {
                RulerScrollViewer.ScrollToHorizontalOffset(TimelineScrollViewer.HorizontalOffset);
                if (e.HorizontalChange != 0)
                {
                    _rulerRedrawTimer.Stop();
                    _rulerRedrawTimer.Start();
                }
            };

            TimelineRulerCanvas.PreviewMouseLeftButtonDown += TimelineRulerCanvas_PreviewMouseLeftButtonDown;
            TimelineCanvas.PreviewMouseMove += TimelineCanvas_PreviewMouseMove;
            TimelineCanvas.PreviewMouseLeftButtonUp += TimelineCanvas_PreviewMouseLeftButtonUp;

            DrawTimelineRuler();
        }

        private void InitializeVideoViews()
        {
            var playerViewModel = _mainViewModel.PlayerViewModel;
            if (playerViewModel.VideoPlayers.Count >= 5)
            {
                videoView0.MediaPlayer = playerViewModel.VideoPlayers[0];
                videoView1.MediaPlayer = playerViewModel.VideoPlayers[1];
                videoView2.MediaPlayer = playerViewModel.VideoPlayers[2];
                videoView3.MediaPlayer = playerViewModel.VideoPlayers[3];
                videoView4.MediaPlayer = playerViewModel.VideoPlayers[4];
            }
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

            //videoView.MediaPlayer = _mainViewModel.PlayerViewModel.MainVideoPlayer;
            InitializeVideoViews();
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
                    //StatusTextBlock.Text = $"{openFileDialog.FileNames.Length}개의 미디어가 목록에 추가되었습니다.";
                }
            }
        }

        private void Timeline_DragOver(object sender, DragEventArgs e)
        {
            if ((DateTime.Now - _lastDragUpdateTime).TotalMilliseconds < DRAG_UPDATE_THROTTLE_MS)
            {
                e.Handled = true;
                return;
            }
            _lastDragUpdateTime = DateTime.Now;

            if (e.Data.GetDataPresent("TimelineClip"))
            {
                e.Effects = DragDropEffects.Move;

                var vm = DataContext as MainViewModel;
                if (vm?.VideoEditor.DraggedClip == null) return;

                Point position = e.GetPosition(TimelineCanvas);
                double deltaX = position.X - vm.VideoEditor.DragStartPoint.X;
                double deltaTime = deltaX / vm.VideoEditor.PixelsPerSecond;
                double newStartPosition = vm.VideoEditor.OriginalClipStartPosition + deltaTime;

                int deltaTrack = (int)Math.Round((position.Y - vm.VideoEditor.DragStartPoint.Y) / 60.0);
                int newTrackIndex = Math.Clamp(vm.VideoEditor.OriginalClipTrackIndex + deltaTrack, 0, 4);

                vm.VideoEditor.DraggedClip.StartPosition = Math.Max(0, newStartPosition);
                vm.VideoEditor.DraggedClip.TrackIndex = newTrackIndex;
                vm.SyncPlayersToTimeline();
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
            if (TimelineRulerCanvas == null || _mainViewModel == null) return;

            TimelineRulerCanvas.Children.Clear();

            double pixelsPerSecond = _mainViewModel.VideoEditor.PixelsPerSecond;
            double totalDuration = _currentTimelineDurationSec;
            double totalWidth = totalDuration * pixelsPerSecond;

            TimelineRulerCanvas.Width = totalWidth;
            TimelineCanvas.Width = totalWidth;

            // Determine the visible time range
            double firstVisibleTime = TimelineScrollViewer.HorizontalOffset / pixelsPerSecond;
            double lastVisibleTime = (TimelineScrollViewer.HorizontalOffset + TimelineScrollViewer.ViewportWidth) / pixelsPerSecond;

            // Add a buffer to each side to ensure smooth scrolling
            firstVisibleTime = Math.Max(0, firstVisibleTime - 20); // 20 seconds buffer
            lastVisibleTime = Math.Min(totalDuration, lastVisibleTime + 20);

            var (majorTickInterval, minorTickInterval, timeFormat) = GetMajorTickInterval(pixelsPerSecond);

            // Align the starting point to the nearest minor tick
            double startSec = Math.Floor(firstVisibleTime / minorTickInterval) * minorTickInterval;
            long startTick = (long)Math.Round(startSec / minorTickInterval);
            int ticksPerMajor = (int)Math.Round(majorTickInterval / minorTickInterval);

            if (ticksPerMajor == 0) ticksPerMajor = 1;

            for (long i = startTick; ; ++i)
            {
                double sec = i * minorTickInterval;
                if (sec > lastVisibleTime) break;
                if (sec < 0) continue;

                bool isMajorTick = (i % ticksPerMajor) == 0;
                double x = sec * pixelsPerSecond;

                var line = new Line
                {
                    X1 = x,
                    X2 = x,
                    Y1 = 0,
                    Y2 = isMajorTick ? 20 : 10,
                    Stroke = isMajorTick ? Brushes.LightGray : Brushes.Gray,
                    StrokeThickness = isMajorTick ? 2 : 1
                };
                TimelineRulerCanvas.Children.Add(line);

                if (isMajorTick)
                {
                    var text = new TextBlock
                    {
                        Text = TimeSpan.FromSeconds(sec).ToString(timeFormat),
                        Foreground = Brushes.White,
                        FontSize = 12
                    };
                    Canvas.SetLeft(text, x + 2);
                    Canvas.SetTop(text, 22);
                    TimelineRulerCanvas.Children.Add(text);
                }
            }
        }

        private (double major, double minor, string format) GetMajorTickInterval(double pixelsPerSecond)
        {
            double visibleWidth = TimelineScrollViewer.ActualWidth;
            if (visibleWidth <= 0) visibleWidth = 1000; // Default width if not rendered yet

            double visibleTime = visibleWidth / pixelsPerSecond;
            double idealMajorTickInterval = visibleTime / 10; // 10 major ticks on screen

            var intervals = new[] { 0.1, 0.2, 0.5, 1, 5, 10, 30, 60, 180, 300, 600, 900, 1800, 3600 };
            var majorTick = intervals.FirstOrDefault(i => i >= idealMajorTickInterval);
            if (majorTick == 0) majorTick = intervals.Last();

            if (majorTick >= 1)
            {
                var minorTickDivider = majorTick >= 1800 ? 6 : 5;
                return (majorTick, majorTick / minorTickDivider, @"h\:mm\:ss");
            }

            return (majorTick, majorTick / 5, @"ss\.f");
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
                });
            }
            else if (e.PropertyName == nameof(MainViewModel.CurrentTimelinePosition))
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

                _mainViewModel.SeekTimeline(clickedTimeSec, isScrubbing: false);

                _playheadLine.X1 = position.X;
                _playheadLine.X2 = position.X;
            }
        }

        private void TimelineRulerCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _mainViewModel.StopPlayback(); // 재생 중이었다면 멈추고 상태 저장
            _mainViewModel.IsScrubbing = true;
            (sender as UIElement)?.CaptureMouse();
            UpdatePlayheadFromMouseEvent(e);
            e.Handled = true;
        }

        private void UpdatePlayheadFromMouseEvent(MouseEventArgs e)
        {
            if ((DateTime.Now - _lastDragUpdateTime).TotalMilliseconds < DRAG_UPDATE_THROTTLE_MS)
            {
                return;
            }
            _lastDragUpdateTime = DateTime.Now;
            Point position = e.GetPosition(TimelineRulerCanvas);
            double clickedTimeSec = position.X / _mainViewModel.VideoEditor.PixelsPerSecond;
            _mainViewModel.SeekTimeline(clickedTimeSec, isScrubbing: true);
        }

        private void ResizeHandle_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var handle = sender as FrameworkElement;
            var clip = handle?.DataContext as TimelineClipBase;
            if (handle == null || clip == null) return;
            TimelineCanvas.CaptureMouse();
            _mainViewModel.VideoEditor.StartClipResize(clip, e.GetPosition(TimelineCanvas));
            e.Handled = true;
        }

        

        private void TimelineCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_mainViewModel.IsScrubbing)
            {
                UpdatePlayheadFromMouseEvent(e);
            }

            else if (_mainViewModel.VideoEditor.IsResizing)
            {
                _mainViewModel.VideoEditor.UpdateClipResize(e.GetPosition(TimelineCanvas));
            }
        }

        private void TimelineCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_mainViewModel.IsScrubbing)
            {
                _mainViewModel.IsScrubbing = false;
                (sender as UIElement)?.ReleaseMouseCapture();
                _mainViewModel.ResumePlaybackIfNeeded(); // 재생 재개
                e.Handled = true;
            }

            else if (_mainViewModel.VideoEditor.IsResizing)
            {
                _mainViewModel.VideoEditor.EndClipResize();   
                (sender as UIElement)?.ReleaseMouseCapture();       
                e.Handled = true;
            }
        }

        private void TimelineResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            double newWidth = TimelineRulerCanvas.Width + e.HorizontalChange;
            double pixelsPerSecond = _mainViewModel.VideoEditor.PixelsPerSecond;
    
            // 최소 너비 제어 (예: 10초에 해당하는 너비)
            double minWidth = 10 * pixelsPerSecond;
            if (newWidth < minWidth)
            {
                newWidth = minWidth;
            }

            _currentTimelineDurationSec = newWidth / pixelsPerSecond;
    
            // MainViewModel의 TotalTimelineDurationMs 업데이트
            _mainViewModel.TotalTimelineDurationMs = (long)(_currentTimelineDurationSec * 1000);

            DrawTimelineRuler();
        }

        private void TimelineDurationTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                (sender as TextBox)?.GetBindingExpression(TextBox.TextProperty).UpdateSource();
                Keyboard.ClearFocus();
            }
        }

        private void TimelineDurationTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            (sender as TextBox)?.GetBindingExpression(TextBox.TextProperty).UpdateSource();
        }

        private void TimelineDurationTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }
    }
}