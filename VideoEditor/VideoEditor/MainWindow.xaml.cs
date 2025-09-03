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

            videoView.MediaPlayer = _mainViewModel.PlayerViewModel.MediaPlayer;

            this.Loaded += MainWindow_Loaded;
            _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;

            //_mainViewModel.PlayerViewModel.MediaPlayer.TimeChanged += MediaPlayer_TimeChanged;
            _mainViewModel.PlayerViewModel.MediaPlayer.LengthChanged += MediaPlayer_LengthChanged;
            _mainViewModel.PlayerViewModel.MediaPlayer.Stopped += MediaPlayer_Stopped;

            TimelineScrollViewer.ScrollChanged += (s, e) =>
            {
                RulerScrollViewer.ScrollToHorizontalOffset(TimelineScrollViewer.HorizontalOffset);
            };

            DrawTimelineRuler();
        }

        private void MainViewModel_ExportStarted(object? sender, ExportStartedEventArgs e)
        {
            // 새 진행률 창 생성
            _progressWindow = new ExportProgressWindow
            {
                // DataContext를 이벤트로 전달받은 ViewModel로 설정
                DataContext = e.ProgressViewModel,
                // 이 창을 주인으로 설정하여 중앙에 표시
                Owner = this
            };

            // 메인 창 비활성화 (렌더링 중 다른 작업 방지)
            this.IsEnabled = false;
            // 모달리스(Modeless)로 창을 띄워 UI 스레드를 막지 않도록 함
            _progressWindow.Show();
        }

        private void MainViewModel_ExportFinished(object? sender, EventArgs e)
        {
            // UI 스레드에서 창을 닫도록 보장
            Dispatcher.Invoke(() =>
            {
                _progressWindow?.Close();
                _progressWindow = null;

                // 메인 창 다시 활성화
                this.IsEnabled = true;
                // 메인 창을 다시 맨 앞으로 가져옴
                this.Activate();
            });
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

        // MainWindow.xaml.cs

        // using System.ComponentModel; // 파일 상단에 이 using 문이 있는지 확인하세요.

        // ... (기존 클래스 선언은 그대로 둡니다)

        // ✅ MainViewModel의 속성 변경을 감지하여 UI를 업데이트하는 중앙 허브 역할의 메서드
        private void MainViewModel_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            // --- 1. 타임라인의 총 길이가 변경되었을 때의 처리 ---
            if (e.PropertyName == nameof(MainViewModel.TotalTimelineDurationMs))
            {
                // UI 스레드에서 실행되도록 보장합니다 (필수).
                Dispatcher.Invoke(() =>
                {
                    // ViewModel의 최신 값을 가져와 로컬 변수를 업데이트합니다.
                    _currentTimelineDurationSec = _mainViewModel.TotalTimelineDurationMs / 1000.0;
                    // 업데이트된 길이로 눈금자를 다시 그립니다.
                    DrawTimelineRuler();

                    // 디버깅 로그 추가
                    System.Diagnostics.Debug.WriteLine($"[UI Event] Ruler updated to: {_currentTimelineDurationSec:F2} seconds.");
                });
            }

            // --- 2. 타임라인의 현재 위치(시간)가 변경되었을 때의 처리 ---
            if (e.PropertyName == nameof(MainViewModel.CurrentTimelinePosition))
            {
                // UI 스레드에서 실행되도록 보장합니다 (필수).
                Dispatcher.Invoke(() =>
                {
                    if (_playheadLine != null)
                    {
                        // ViewModel의 현재 시간(초)을 가져와 픽셀 위치로 변환합니다.
                        double newX = _mainViewModel.CurrentTimelinePosition * _mainViewModel.VideoEditor.PixelsPerSecond;

                        // 플레이헤드 라인의 X 좌표를 업데이트하여 시각적으로 움직입니다.
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
                // 배속 범위 제한 (0.1 ~ 25.0)
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
            // 배속 컨트롤 패널과 비디오 정보 패널을 토글
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