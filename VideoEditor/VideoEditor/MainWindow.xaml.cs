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
        private bool _isResizing = false;
        private Point _resizeStartPoint;
        private VideoLayerViewModel? _resizingLayer;
        private bool _isDragging = false;
        private Point _layerDragStartPoint;
        private VideoLayerViewModel? _draggingLayer;
        public MainWindow()
        {
            InitializeComponent();

            Common.UIDispatcher.Initialize();
            _mainViewModel = new MainViewModel();
            DataContext = _mainViewModel;


            this.Loaded += MainWindow_Loaded;
            InitializeClipPropertyControls();
            
            // 클립 선택 이벤트 연결
            _mainViewModel.PlayerViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(PlayerViewModel.SelectedLayer))
                {
                    if (_mainViewModel.PlayerViewModel.SelectedLayer != null)
                    {
                        ShowClipPropertiesPanel();
                        UpdateClipPropertyControls();
                    }
                    else
                    {
                        HideClipPropertiesPanel();
                    }
                }
            };

            // 타임라인 클립 선택 이벤트 연결
            _mainViewModel.VideoEditor.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(VideoEditorViewModel.SelectedClip))
                {
                    var selectedClip = _mainViewModel.VideoEditor.SelectedClip;
                    if (selectedClip?.AssociatedLayer != null)
                    {
                        _mainViewModel.PlayerViewModel.SelectedLayer = selectedClip.AssociatedLayer;
                    }
                }
            };

            // 크기 조절 시작 이벤트 연결
            _mainViewModel.PlayerViewModel.OnStartResize += (s, e) =>
            {
                _isResizing = true;
                _resizingLayer = e.Layer;
                _resizeStartPoint = Mouse.GetPosition(this);
                this.CaptureMouse();
            };

            // 드래그 시작 이벤트 연결
            _mainViewModel.PlayerViewModel.OnStartDrag += (s, e) =>
            {
                _isDragging = true;
                _draggingLayer = e.Layer;
                _layerDragStartPoint = Mouse.GetPosition(this);
                this.CaptureMouse();
            };

            // 소프트 클록 이벤트로 플레이헤드/길이 업데이트
            _mainViewModel.PlayerViewModel.ClockTimeChanged += (_, __) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_playheadLine == null) return;
                    double currentSeconds = _mainViewModel.PlayerViewModel.CurrentTime / 1000.0;
                    double newX = currentSeconds * _mainViewModel.VideoEditor.PixelsPerSecond;
                    _playheadLine.X1 = newX;
                    _playheadLine.X2 = newX;
                }));
            };
            _mainViewModel.PlayerViewModel.ClockLengthChanged += (_, __) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    double videoDurationSec = _mainViewModel.PlayerViewModel.TotalDuration / 1000.0;
                    _currentTimelineDurationSec = Math.Max(300.0, videoDurationSec);
                    DrawTimelineRuler();
                }));
            };
            _mainViewModel.PlayerViewModel.Stopped += (_, __) =>
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_playheadLine != null)
                    {
                        _playheadLine.X1 = 0;
                        _playheadLine.X2 = 0;
                    }
                }));
            };

            TimelineScrollViewer.ScrollChanged += (s, e) =>
            {
                RulerScrollViewer.ScrollToHorizontalOffset(TimelineScrollViewer.HorizontalOffset);
            };

            DrawTimelineRuler();
            
            // 마우스 이벤트 처리
            this.MouseMove += MainWindow_MouseMove;
            this.MouseLeftButtonUp += MainWindow_MouseLeftButtonUp;
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
            _dragStartPoint = e.GetPosition(null); // 마우스 클릭 시작 지점 저장
            if (e.OriginalSource is DependencyObject source)
            {
                // 찾은 UI 요소에서 가장 가까운 ListBoxItem을 찾습니다.
                var listBoxItem = source.FindAncestor<ListBoxItem>();
                if (listBoxItem != null)
                {
                    // 그 ListBoxItem에 해당하는 Myvideo 객체를 드래그 대상으로 확정합니다.
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

        private void InitializeClipPropertyControls()
        {
            // 슬라이더 이벤트 연결
            PositionXSlider.ValueChanged += (s, e) => UpdateSelectedClipProperties();
            PositionYSlider.ValueChanged += (s, e) => UpdateSelectedClipProperties();
            WidthSlider.ValueChanged += (s, e) => UpdateSelectedClipProperties();
            HeightSlider.ValueChanged += (s, e) => UpdateSelectedClipProperties();
            ClipSpeedSlider.ValueChanged += (s, e) => UpdateSelectedClipProperties();
            RotationSlider.ValueChanged += (s, e) => UpdateSelectedClipProperties();
            OpacitySlider.ValueChanged += (s, e) => UpdateSelectedClipProperties();

            // 값 표시 업데이트
            PositionXSlider.ValueChanged += (s, e) => PositionXValue.Text = ((int)e.NewValue).ToString();
            PositionYSlider.ValueChanged += (s, e) => PositionYValue.Text = ((int)e.NewValue).ToString();
            WidthSlider.ValueChanged += (s, e) => WidthValue.Text = ((int)e.NewValue).ToString();
            HeightSlider.ValueChanged += (s, e) => HeightValue.Text = ((int)e.NewValue).ToString();
            ClipSpeedSlider.ValueChanged += (s, e) => ClipSpeedValue.Text = $"{e.NewValue:F1}x";
            RotationSlider.ValueChanged += (s, e) => RotationValue.Text = $"{e.NewValue:F0}°";
            OpacitySlider.ValueChanged += (s, e) => OpacityValue.Text = $"{(int)(e.NewValue * 100)}%";
        }

        private void UpdateSelectedClipProperties()
        {
            var selectedLayer = _mainViewModel.PlayerViewModel.SelectedLayer;
            if (selectedLayer != null)
            {
                selectedLayer.Left = PositionXSlider.Value;
                selectedLayer.Top = PositionYSlider.Value;
                selectedLayer.Width = WidthSlider.Value;
                selectedLayer.Height = HeightSlider.Value;
                selectedLayer.PlaybackRate = ClipSpeedSlider.Value;
                selectedLayer.Rotation = RotationSlider.Value;
                selectedLayer.Opacity = OpacitySlider.Value;
            }
        }

        private void ShowClipPropertiesPanel()
        {
            ClipPropertiesPanel.Visibility = Visibility.Visible;
            VideoInfoPanel.Visibility = Visibility.Collapsed;
            SpeedControlPanel.Visibility = Visibility.Collapsed;
        }

        private void HideClipPropertiesPanel()
        {
            ClipPropertiesPanel.Visibility = Visibility.Collapsed;
            VideoInfoPanel.Visibility = Visibility.Visible;
        }

        private void UpdateClipPropertyControls()
        {
            var selectedLayer = _mainViewModel.PlayerViewModel.SelectedLayer;
            if (selectedLayer != null)
            {
                PositionXSlider.Value = selectedLayer.Left;
                PositionYSlider.Value = selectedLayer.Top;
                WidthSlider.Value = selectedLayer.Width;
                HeightSlider.Value = selectedLayer.Height;
                ClipSpeedSlider.Value = selectedLayer.PlaybackRate;
                RotationSlider.Value = selectedLayer.Rotation;
                OpacitySlider.Value = selectedLayer.Opacity;
            }
        }

        private void MainWindow_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isResizing && _resizingLayer != null)
            {
                var currentPosition = e.GetPosition(this);
                var deltaX = currentPosition.X - _resizeStartPoint.X;
                var deltaY = currentPosition.Y - _resizeStartPoint.Y;

                // 크기 조절 (최소 크기 제한)
                var newWidth = Math.Max(50, _resizingLayer.Width + deltaX);
                var newHeight = Math.Max(50, _resizingLayer.Height + deltaY);

                _resizingLayer.Width = newWidth;
                _resizingLayer.Height = newHeight;

                // 슬라이더 값도 업데이트
                WidthSlider.Value = newWidth;
                HeightSlider.Value = newHeight;
                WidthValue.Text = ((int)newWidth).ToString();
                HeightValue.Text = ((int)newHeight).ToString();

                _resizeStartPoint = currentPosition;
            }
            else if (_isDragging && _draggingLayer != null)
            {
                var currentPosition = e.GetPosition(this);
                var deltaX = currentPosition.X - _layerDragStartPoint.X;
                var deltaY = currentPosition.Y - _layerDragStartPoint.Y;

                // 위치 조절
                var newLeft = Math.Max(0, _draggingLayer.Left + deltaX);
                var newTop = Math.Max(0, _draggingLayer.Top + deltaY);

                _draggingLayer.Left = newLeft;
                _draggingLayer.Top = newTop;

                // 슬라이더 값도 업데이트
                PositionXSlider.Value = newLeft;
                PositionYSlider.Value = newTop;
                PositionXValue.Text = ((int)newLeft).ToString();
                PositionYValue.Text = ((int)newTop).ToString();

                _layerDragStartPoint = currentPosition;
            }
        }

        private void MainWindow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_isResizing)
            {
                _isResizing = false;
                _resizingLayer = null;
                this.ReleaseMouseCapture();
            }
            else if (_isDragging)
            {
                _isDragging = false;
                _draggingLayer = null;
                this.ReleaseMouseCapture();
            }
        }
    }
}