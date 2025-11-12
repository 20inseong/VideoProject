using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using LibVLCSharp.Shared;
using Microsoft.Win32;
using VideoEditor.Common;
using VideoEditor.Models;
using VideoEditor.ViewModels;
using VideoEditor.Views;

namespace VideoEditor
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool bRedraw);

        [DllImport("gdi32.dll")]
        private static extern IntPtr CreateRectRgn(int nLeftRect, int nTopRect, int nRightRect, int nBottomRect);

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string lpszWindow);

        [DllImport("user32.dll")]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumChildProc lpEnumFunc, IntPtr lParam);

        private delegate bool EnumChildProc(IntPtr hwnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private MainViewModel _mainViewModel;
        private ExportProgressWindow? _progressWindow;
        private OverlayWindow _overlayWindow;
        private Myvideo _draggedVideo = null;
        private Point _dragStartPoint;
        private Line _playheadLine;
        private double _currentTimelineDurationSec = 300;
        private DateTime _lastDragUpdateTime = DateTime.MinValue;
        private const int DRAG_UPDATE_THROTTLE_MS = 10; // 10ms 간격으로 업데이트
        private DispatcherTimer _rulerRedrawTimer;
        private DispatcherTimer _videoClippingTimer;
        private DispatcherTimer _deactivationCheckTimer; 
        private DispatcherTimer _focusCheckTimer;
        private bool _needsZOrderUpdate = false;
        private bool _isOverlayInteractionActive = false;
        private bool _wasApplicationActive = true;
        
        private bool _isSelectingWithRectangle = false;
        private Point _selectionStartPoint;
        private Rectangle _selectionRectangle;
        private List<TimelineClipBase> _clipsBeforeSelection = new List<TimelineClipBase>();

        private List<TimelineClipBase>? _switchSavedActiveWpfOverlays;
        private bool _switchWasOverlayVisible;

        public void ClearSnapIndicators()
        {
            SnapIndicatorCanvas.Children.Clear();
        }

        public void CancelDeactivationTimer()
        {
            if (_deactivationCheckTimer != null && _deactivationCheckTimer.IsEnabled)
            {
                _deactivationCheckTimer.Stop();
                _switchSavedActiveWpfOverlays = null;
                _switchWasOverlayVisible = false;
            }
        }

        public void SetOverlayInteractionActive(bool isActive)
        {
            _isOverlayInteractionActive = isActive;
        }


        public MainWindow()
        {
            InitializeComponent();
            FontManager.LoadValidFonts();

            Common.UIDispatcher.Initialize();
            _mainViewModel = new MainViewModel(this);
            DataContext = _mainViewModel;

            _mainViewModel.ExportStarted += MainViewModel_ExportStarted;

            this.Loaded += MainWindow_Loaded;
            this.Activated += MainWindow_Activated;
            this.Deactivated += MainWindow_Deactivated;
            _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;

            _mainViewModel.VideoEditor.PropertyChanged += VideoEditor_PropertyChanged;
            
            this.AddHandler(MenuItem.SubmenuOpenedEvent, new RoutedEventHandler(Menu_SubmenuOpened));
            this.AddHandler(MenuItem.SubmenuClosedEvent, new RoutedEventHandler(Menu_SubmenuClosed));
            
            _mainViewModel.VideoEditor.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(VideoEditorViewModel.IsDraggingClip))
                {
                    if (_mainViewModel.VideoEditor.IsDraggingClip)
                    {
                        _videoClippingTimer.Interval = TimeSpan.FromMilliseconds(16);
                        _needsZOrderUpdate = true;
                    }
                    else
                    {
                        _videoClippingTimer.Interval = TimeSpan.FromMilliseconds(100);
                        ClipVideoViewsToPlayerHost();
                    }
                }
            };
            
            _mainViewModel.VideoClipZOrderChanged += (s, e) =>
            {
                _needsZOrderUpdate = true;
            };
            
            _mainViewModel.ActiveVideoClips.CollectionChanged += (s, e) => 
            {
                _needsZOrderUpdate = true;
                Dispatcher.BeginInvoke(new Action(() => 
                {
                    ClipVideoViewsToPlayerHost();
                }), DispatcherPriority.Loaded);
            };
            
            _mainViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.ActiveVideoClips))
                {
                    _needsZOrderUpdate = true;
                    Dispatcher.BeginInvoke(new Action(() => ClipVideoViewsToPlayerHost()), DispatcherPriority.Loaded);
                }
            };
            
            _videoClippingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _videoClippingTimer.Tick += (s, e) =>
            {
                ClipVideoViewsToPlayerHost();
                BringOverlayToFront();
                BringProgressWindowsToFront();
            };
            _videoClippingTimer.Start();

            _overlayWindow = new OverlayWindow
            {
                DataContext = _mainViewModel
            };

            LocationChanged += UpdateOverlayPosition;
            SizeChanged += UpdateOverlayPosition;
            VideoPlayerHost.SizeChanged += UpdateOverlayPosition;
            
            this.Loaded += (s, e) =>
            {
                var previewViewbox = this.FindName("PreviewViewbox") as FrameworkElement;
                if (previewViewbox != null)
                {
                    previewViewbox.SizeChanged += UpdateOverlayPosition;
                }
            };

            _rulerRedrawTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            _rulerRedrawTimer.Tick += (s, e) =>
            {
                _rulerRedrawTimer.Stop();
                DrawTimelineRuler();
            };
            
            _deactivationCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
            _deactivationCheckTimer.Tick += DeactivationCheckTimer_Tick;
            
            _focusCheckTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _focusCheckTimer.Tick += FocusCheckTimer_Tick;
            _focusCheckTimer.Start();

            TimelineScrollViewer.ScrollChanged += (s, e) =>
            {
                RulerScrollViewer.ScrollToHorizontalOffset(TimelineScrollViewer.HorizontalOffset);
                
                if (e.VerticalChange != 0)
                {
                    TrackLabelsScrollViewer.ScrollToVerticalOffset(TimelineScrollViewer.VerticalOffset);
                }
                
                if (e.HorizontalChange != 0)
                {
                    DrawTimelineRuler();
                }
            };

            TimelineRulerCanvas.PreviewMouseLeftButtonDown += TimelineRulerCanvas_PreviewMouseLeftButtonDown;
            TimelineRulerCanvas.PreviewMouseMove += TimelineRulerCanvas_PreviewMouseMove;
            TimelineRulerCanvas.PreviewMouseLeftButtonUp += TimelineRulerCanvas_PreviewMouseLeftButtonUp;

            PlayheadCanvas.PreviewMouseLeftButtonDown += PlayheadCanvas_PreviewMouseLeftButtonDown;
            PlayheadCanvas.PreviewMouseMove += PlayheadCanvas_PreviewMouseMove;
            PlayheadCanvas.PreviewMouseLeftButtonUp += PlayheadCanvas_PreviewMouseLeftButtonUp;

            TimelineCanvas.PreviewMouseMove += TimelineCanvas_PreviewMouseMove;
            TimelineCanvas.PreviewMouseLeftButtonUp += TimelineCanvas_PreviewMouseLeftButtonUp;

            VideoPlayerHost.SizeChanged += (s, e) =>
            {
                _mainViewModel.PlayerHostWidth = 1920;
                _mainViewModel.PlayerHostHeight = 1080;
            };

            DrawTimelineRuler();
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.OriginalSource is TextBox || e.OriginalSource is Slider)
            {
                return;
            }

            // Alt 키와 함께 방향키: 타임라인 스크롤
            if (Keyboard.Modifiers == ModifierKeys.Alt && (e.Key == Key.Left || e.Key == Key.Right))
            {
                double scrollAmount = 50.0;
                double newOffset = TimelineScrollViewer.HorizontalOffset + (e.Key == Key.Left ? -scrollAmount : scrollAmount);
                TimelineScrollViewer.ScrollToHorizontalOffset(newOffset);
                e.Handled = true;
            }
            // 방향키만: 선택된 클립 이동
            else if (Keyboard.Modifiers == ModifierKeys.None && (e.Key == Key.Left || e.Key == Key.Right))
            {
                // ViewModel의 Command를 직접 호출합니다.
                if (_mainViewModel.VideoEditor.MoveClipsByKeyCommand.CanExecute(e.Key))
                {
                    _mainViewModel.VideoEditor.MoveClipsByKeyCommand.Execute(e.Key);
                    e.Handled = true; // 이벤트 처리를 완료했음을 알립니다.
                }
            }
        }

        private void NewProject_Click(object sender, RoutedEventArgs e)
        {
            var newWindow = new MainWindow();

            newWindow.Show();
        }

        private void UpdateOverlayPosition(object? sender, EventArgs e)
        {
            var previewViewbox = this.FindName("PreviewViewbox") as FrameworkElement;
            
            if (previewViewbox != null && previewViewbox.IsVisible && this.IsVisible && 
                previewViewbox.ActualWidth > 0 && previewViewbox.ActualHeight > 0)
            {
                try
                {
                    Point location = previewViewbox.PointToScreen(new Point(0, 0));
                    _overlayWindow.Left = location.X;
                    _overlayWindow.Top = location.Y;
                    _overlayWindow.Width = previewViewbox.ActualWidth;
                    _overlayWindow.Height = previewViewbox.ActualHeight;
                    
                    double scaleX = previewViewbox.ActualWidth / 1920.0;
                    double scaleY = previewViewbox.ActualHeight / 1080.0;
                    
                    _overlayWindow.SetScale(scaleX, scaleY);
                    
                    BringOverlayToFront();
                    
                    Dispatcher.BeginInvoke(new Action(() => ClipVideoViewsToPlayerHost()), DispatcherPriority.Normal);
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[UpdateOverlayPosition] Error: {ex.Message}");
                }
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOP = new IntPtr(0);
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;
        
        private bool _isMenuOpen = false;

        private void ClipVideoViewsToPlayerHost()
        {
            var previewViewbox = this.FindName("PreviewViewbox") as FrameworkElement;
            
            if (_mainViewModel?.ActiveVideoClips == null || previewViewbox == null)
                return;

            if (previewViewbox.ActualWidth <= 0 || previewViewbox.ActualHeight <= 0)
                return;

            var source = PresentationSource.FromVisual(this);
            if (source == null || source.CompositionTarget == null) return;

            // M11 = 수평 DPI 배율, M22 = 수직 DPI 배율
            var dpiX = source.CompositionTarget.TransformToDevice.M11;
            var dpiY = source.CompositionTarget.TransformToDevice.M22;

            var hostBoundsDIP = new Rect(
                previewViewbox.PointToScreen(new Point(0, 0)),
                new Size(previewViewbox.ActualWidth, previewViewbox.ActualHeight)
            );

            // DIPs를 실제 물리적 픽셀로 변환
            var hostBoundsPixels = new Rect(
                hostBoundsDIP.X * dpiX,
                hostBoundsDIP.Y * dpiY,
                hostBoundsDIP.Width * dpiX,
                hostBoundsDIP.Height * dpiY
            );

            var hwndHosts = FindVisualChildren<System.Windows.Interop.HwndHost>(VideoPlayerHost).ToList();

            // Z-order update (only when needed)
            if (_needsZOrderUpdate && hwndHosts.Count > 0)
            {
                UpdateVideoZOrder(hwndHosts);
                _needsZOrderUpdate = false;
            }

            // Clipping update (always)
            ApplyClippingToVideoWindows(hwndHosts, hostBoundsPixels, dpiX, dpiY);
        }

        private void UpdateVideoZOrder(List<System.Windows.Interop.HwndHost> hwndHosts)
        {
            System.Diagnostics.Debug.WriteLine($"[Z-Order] Updating Z-order for {hwndHosts.Count} HwndHost controls");

            var hwndToClipMap = new Dictionary<IntPtr, VideoClip>();

            foreach (var hwndHost in hwndHosts)
            {
                try
                {
                    IntPtr parentHwnd = hwndHost.Handle;
                    if (parentHwnd == IntPtr.Zero) continue;

                    var frameworkElement = hwndHost as FrameworkElement;
                    if (frameworkElement?.DataContext is VideoClip videoClip)
                    {
                        hwndToClipMap[parentHwnd] = videoClip;
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Z-Order] Mapping error: {ex.Message}");
                }
            }

            var sortedClips = hwndToClipMap.OrderBy(kvp => kvp.Value.TrackIndex).ToList();

            IntPtr overlayHwnd = IntPtr.Zero;
            if (_overlayWindow != null && _overlayWindow.IsLoaded)
            {
                overlayHwnd = new WindowInteropHelper(_overlayWindow).Handle;
            }

            IntPtr insertAfter = overlayHwnd != IntPtr.Zero ? overlayHwnd : HWND_TOP;
            
            for (int i = sortedClips.Count - 1; i >= 0; i--)
            {
                var kvp = sortedClips[i];
                IntPtr hwnd = kvp.Key;
                
                SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                insertAfter = hwnd;
            }
            
            if (overlayHwnd != IntPtr.Zero)
            {
                SetWindowPos(overlayHwnd, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
        }

        private void ApplyClippingToVideoWindows(List<System.Windows.Interop.HwndHost> hwndHosts, Rect hostBoundsPixels, double dpiX, double dpiY)
        {
            foreach (var hwndHost in hwndHosts)
            {
                try
                {
                    IntPtr parentHwnd = hwndHost.Handle;
                    if (parentHwnd == IntPtr.Zero) continue;

                    // HwndHost의 좌표를 픽셀 단위로 변환
                    var hwndHostPosDIP = hwndHost.PointToScreen(new Point(0, 0));
                    var hwndHostBoundsDIP = new Rect(hwndHostPosDIP, new Size(hwndHost.ActualWidth, hwndHost.ActualHeight));

                    var hwndHostBoundsPixels = new Rect(
                        hwndHostBoundsDIP.X * dpiX,
                        hwndHostBoundsDIP.Y * dpiY,
                        hwndHostBoundsDIP.Width * dpiX,
                        hwndHostBoundsDIP.Height * dpiY
                    );

                    // 물리적 픽셀을 기준으로 교차 영역 계산
                    var intersection = Rect.Intersect(hostBoundsPixels, hwndHostBoundsPixels);

                    bool isCompletelyOutside = intersection.IsEmpty || intersection.Width <= 0 || intersection.Height <= 0;
                    
                    if (!isCompletelyOutside && intersection.Width > 0 && intersection.Height > 0)
                    {
                        // 교차 영역을 HwndHost 기준 상대 픽셀 좌표로 변환
                        int clipLeft = Math.Max(0, (int)(intersection.Left - hwndHostBoundsPixels.Left));
                        int clipTop = Math.Max(0, (int)(intersection.Top - hwndHostBoundsPixels.Top));
                        int clipRight = (int)(intersection.Right - hwndHostBoundsPixels.Left);
                        int clipBottom = (int)(intersection.Bottom - hwndHostBoundsPixels.Top);

                        if (clipRight > clipLeft && clipBottom > clipTop)
                        {
                            IntPtr hRgn = CreateRectRgn(clipLeft, clipTop, clipRight, clipBottom);
                            if (hRgn != IntPtr.Zero)
                            {
                                SetWindowRgn(parentHwnd, hRgn, true);
                            }

                            // Apply to child windows
                            EnumChildWindows(parentHwnd, (hwnd, lParam) =>
                            {
                                IntPtr childRgn = CreateRectRgn(clipLeft, clipTop, clipRight, clipBottom);
                                if (childRgn != IntPtr.Zero)
                                {
                                    SetWindowRgn(hwnd, childRgn, true);
                                }
                                return true;
                            }, IntPtr.Zero);
                        }
                        else
                        {
                            HideWindow(parentHwnd);
                        }
                    }
                    else
                    {
                        HideWindow(parentHwnd);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[Clip] Error: {ex.Message}");
                }
            }
        }

        private void HideWindow(IntPtr parentHwnd)
        {
            IntPtr emptyRgn = CreateRectRgn(0, 0, 0, 0);
            if (emptyRgn != IntPtr.Zero)
            {
                SetWindowRgn(parentHwnd, emptyRgn, true);
                
                EnumChildWindows(parentHwnd, (hwnd, lParam) =>
                {
                    IntPtr childEmptyRgn = CreateRectRgn(0, 0, 0, 0);
                    if (childEmptyRgn != IntPtr.Zero)
                    {
                        SetWindowRgn(hwnd, childEmptyRgn, true);
                    }
                    return true;
                }, IntPtr.Zero);
            }
        }

        public void ForceClipVideoViews()
        {
            ClipVideoViewsToPlayerHost();
        }

        private static IEnumerable<T> FindVisualChildren<T>(DependencyObject depObj) where T : DependencyObject
        {
            if (depObj != null)
            {
                for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
                {
                    DependencyObject child = VisualTreeHelper.GetChild(depObj, i);
                    if (child != null && child is T)
                    {
                        yield return (T)child;
                    }

                    foreach (T childOfChild in FindVisualChildren<T>(child))
                    {
                        yield return childOfChild;
                    }
                }
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            _overlayWindow?.Close();
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
                _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
                if (_mainViewModel.VideoEditor != null)
                {
                    _mainViewModel.VideoEditor.PropertyChanged -= VideoEditor_PropertyChanged;
                }
            }

            _mainViewModel = new MainViewModel(this);
            DataContext = _mainViewModel;

            _mainViewModel.ExportStarted += MainViewModel_ExportStarted;
            _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
            _mainViewModel.VideoEditor.PropertyChanged += VideoEditor_PropertyChanged;

            DrawTimelineRuler();
            System.Diagnostics.Debug.WriteLine("[Project] A new ViewModel has been initialized.");
        }

        private void Project_Reset_Click(object sender, RoutedEventArgs e)
        {
            _mainViewModel.HidePreviewObjectsForModal();
            var result = MessageBox.Show("현재 프로젝트의 초기화를 시작하시겠습니까? 저장하지 않은 내용은 사라집니다.",
                                         "새 프로젝트",
                                         MessageBoxButton.YesNo,
                                         MessageBoxImage.Question);
            _mainViewModel.RestorePreviewObjectsAfterModal();

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

            _progressWindow.Closed += (s, args) =>
            {
                this.IsEnabled = true;
                this.Activate();
                _progressWindow = null;
            };

            this.IsEnabled = false;
            _progressWindow.Show();
        }

        private async void btnSelectMedia_Click(object sender, RoutedEventArgs e)
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

            _mainViewModel.HidePreviewObjectsForModal();
            bool? dialogResult = openFileDialog.ShowDialog();
            _mainViewModel.RestorePreviewObjectsAfterModal();

            Common.UIDispatcher.InvokeAsync(async () =>
            {
                _mainViewModel.PlayerViewModel.Stop();
                
                _mainViewModel.ActiveVideoClips.Clear();
                _mainViewModel.ActiveWpfOverlays.Clear();
                
                await Task.Delay(100);
                
                _mainViewModel.SyncPlayersToTimeline();
            });

            if (dialogResult == true)
            {
                var invalidFiles = new List<string>();
                int addedCount = 0;

                this.Cursor = Cursors.Wait;

                foreach (string filePath in openFileDialog.FileNames)
                {
                    string extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
                    bool isValid = true; 

                    if (extension is ".mp4" or ".avi" or ".mkv" or ".mov" or ".wmv")
                    {
                        try
                        {
                            // LibVLC를 사용하여 파일 유효성 검사
                            using (var media = new LibVLCSharp.Shared.Media(_mainViewModel.PlayerViewModel._libVLC, new Uri(filePath)))
                            {
                                await media.Parse(MediaParseOptions.ParseNetwork);
                                if (media.Duration <= 0 || !media.Tracks.Any(t => t.TrackType == LibVLCSharp.Shared.TrackType.Video))
                                {
                                    isValid = false;
                                }
                            }
                        }
                        catch
                        {
                            isValid = false; // 파싱 중 예외 발생 시 유효하지 않음
                        }
                    }

                    // 유효한 파일만 목록에 추가
                    if (isValid)
                    {
                        string fileTitle = System.IO.Path.GetFileNameWithoutExtension(filePath);
                        Myvideo newMedia = new Myvideo
                        {
                            Title = fileTitle,
                            FullPath = filePath,
                            Category = "사용자 추가"
                        };
                        _mainViewModel.VideoList.AddVideo(newMedia);
                        addedCount++;
                    }
                    else
                    {
                        invalidFiles.Add(System.IO.Path.GetFileName(filePath));
                    }
                }

                this.Cursor = Cursors.Arrow;

                if (invalidFiles.Any())
                {
                    string message = $"선택한 파일 중 다음 {invalidFiles.Count}개는 손상되었거나 지원하지 않는 형식으로, 목록에서 제외되었습니다:\n\n" +
                                     string.Join("\n", invalidFiles);
                    _mainViewModel.HidePreviewObjectsForModal();
                    MessageBox.Show(this, message, "파일 추가 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                    _mainViewModel.RestorePreviewObjectsAfterModal();
                }

                if (addedCount > 0)
                {
                    _mainViewModel.VideoList.SelectedVideoItem = _mainViewModel.VideoList.MyVideoes.LastOrDefault();
                }
            }
        }

        private void Timeline_DragOver(object sender, DragEventArgs e)
        {
            var vm = DataContext as MainViewModel;
            if (vm == null) return;

            SnapIndicatorCanvas.Children.Clear();

            if (e.Data.GetDataPresent("TimelineClips") && e.Data.GetData("TimelineClips") is List<TimelineClipBase> draggedClips && draggedClips.Any())
            {
                e.Effects = DragDropEffects.Move;

                // 드래그 업데이트 간격 조절
                if ((DateTime.Now - _lastDragUpdateTime).TotalMilliseconds < 16) // 16ms 간격
                {
                    e.Handled = true;
                    return;
                }
                _lastDragUpdateTime = DateTime.Now;

                Point position = e.GetPosition(TimelineCanvas);
                double mouseDeltaX = position.X - vm.VideoEditor.DragStartPoint.X;
                double timeDelta = mouseDeltaX / vm.VideoEditor.PixelsPerSecond;
                int trackDelta = (int)Math.Round((position.Y - vm.VideoEditor.DragStartPoint.Y) / 60.0);

                const double SNAP_TOLERANCE_PX = 10; 
                double snapToleranceTime = SNAP_TOLERANCE_PX / vm.VideoEditor.PixelsPerSecond;
                double bestSnapOffset = double.MaxValue;

                // 스냅 대상 지점들 수집 
                var snapPoints = new List<double> { 0.0, vm.CurrentTimelinePosition };
                var otherClips = vm.VideoEditor.TimelineClips.Except(draggedClips);
                foreach (var clip in otherClips)
                {
                    snapPoints.Add(clip.StartPosition);
                    snapPoints.Add(clip.StartPosition + clip.Duration);
                }

                var primaryClip = draggedClips.First(); // 그룹의 기준이 될 클립
                var primaryClipOriginalState = vm.VideoEditor.DraggedClipsOriginalState[primaryClip];
                double desiredStartTime = primaryClipOriginalState.OriginalStart + timeDelta;
                double desiredEndTime = desiredStartTime + primaryClip.Duration;

                // 가장 가까운 스냅 지점 찾기
                foreach (double snapPoint in snapPoints.Distinct().OrderBy(p => p))
                {
                    if (Math.Abs(desiredStartTime - snapPoint) < Math.Abs(bestSnapOffset))
                    {
                        bestSnapOffset = snapPoint - desiredStartTime;
                    }
                    if (Math.Abs(desiredEndTime - snapPoint) < Math.Abs(bestSnapOffset))
                    {
                        bestSnapOffset = snapPoint - desiredEndTime;
                    }
                }

                double snappedTimeDelta = timeDelta;
                // 찾은 가장 가까운 스냅 지점이 허용 오차 이내라면, 위치 보정
                if (Math.Abs(bestSnapOffset) < snapToleranceTime)
                {
                    snappedTimeDelta += bestSnapOffset;

                    // 시각적 안내선 그리기
                    double snapLineX = (primaryClipOriginalState.OriginalStart + snappedTimeDelta) * vm.VideoEditor.PixelsPerSecond;
                    if (Math.Abs(desiredEndTime - (primaryClipOriginalState.OriginalStart + snappedTimeDelta + primaryClip.Duration)) > 0.001)
                    {
                        snapLineX += primaryClip.Width;
                    }
                    var snapLine = new Line
                    {
                        X1 = snapLineX,
                        Y1 = 0,
                        X2 = snapLineX,
                        Y2 = TimelineCanvas.ActualHeight,
                        Stroke = Brushes.Red,
                        StrokeThickness = 1.5,
                        StrokeDashArray = new DoubleCollection { 4, 2 }
                    };
                    SnapIndicatorCanvas.Children.Add(snapLine);
                }

                foreach (var clip in draggedClips)
                {
                    if (vm.VideoEditor.DraggedClipsOriginalState.TryGetValue(clip, out var originalState))
                    {
                        double finalStartPosition = originalState.OriginalStart + snappedTimeDelta;
                        int finalTrackIndex = Math.Clamp(originalState.OriginalTrack + trackDelta, 0, 8);

                        clip.StartPosition = Math.Max(0, finalStartPosition);
                        clip.TrackIndex = finalTrackIndex;
                    }
                }

                HandleTimelineAutoScroll(e);

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

        private void HandleTimelineAutoScroll(DragEventArgs e)
        {
            const double scrollMargin = 50;
            const double scrollSpeed = 15;
            const double verticalScrollSpeed = 10;
            
            Point positionInScrollViewer = e.GetPosition(TimelineScrollViewer);
            
            if (positionInScrollViewer.X < scrollMargin && TimelineScrollViewer.HorizontalOffset > 0)
            {
                TimelineScrollViewer.ScrollToHorizontalOffset(TimelineScrollViewer.HorizontalOffset - scrollSpeed);
            }
            else if (positionInScrollViewer.X > TimelineScrollViewer.ActualWidth - scrollMargin)
            {
                TimelineScrollViewer.ScrollToHorizontalOffset(TimelineScrollViewer.HorizontalOffset + scrollSpeed);
            }
            
            if (positionInScrollViewer.Y < scrollMargin && TimelineScrollViewer.VerticalOffset > 0)
            {
                TimelineScrollViewer.ScrollToVerticalOffset(TimelineScrollViewer.VerticalOffset - verticalScrollSpeed);
            }
            else if (positionInScrollViewer.Y > TimelineScrollViewer.ActualHeight - scrollMargin)
            {
                TimelineScrollViewer.ScrollToVerticalOffset(TimelineScrollViewer.VerticalOffset + verticalScrollSpeed);
            }
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

            double firstVisibleTime = TimelineScrollViewer.HorizontalOffset / pixelsPerSecond;
            double lastVisibleTime = (TimelineScrollViewer.HorizontalOffset + TimelineScrollViewer.ViewportWidth) / pixelsPerSecond;

            firstVisibleTime = Math.Max(0, firstVisibleTime - 20);
            lastVisibleTime = Math.Min(totalDuration, lastVisibleTime + 20);

            var (majorTickInterval, minorTickInterval, timeFormat) = GetMajorTickInterval(pixelsPerSecond);

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
            if (visibleWidth <= 0) visibleWidth = 1000;

            double visibleTime = visibleWidth / pixelsPerSecond;
            double idealMajorTickInterval = visibleTime / 10;

            var intervals = new[] { 0.1, 0.2, 0.5, 1, 5, 10, 30, 60, 180, 300, 600, 900, 1800, 3600 };
            var majorTick = intervals.FirstOrDefault(i => i >= idealMajorTickInterval);
            if (majorTick == 0) majorTick = intervals.Last();

            if (majorTick >= 1)
            {
                var minorTickDivider = majorTick >= 1800 ? 6 : 5;
                return (majorTick, majorTick / minorTickDivider, @"h\:mm\:ss");
            }

            return (majorTick, majorTick / 5, @"ss\.ff");
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _overlayWindow.Owner = this;
            _overlayWindow.Show();
            
            // OverlayWindow를 항상 비디오 HwndHost 위에 유지
            BringOverlayToFront();
            
            Dispatcher.BeginInvoke(new Action(() => 
            {
                UpdateOverlayPosition(null, null);
            }), DispatcherPriority.Loaded);

            InitializePlayhead();
            DrawTimelineRuler();
        }

        private void FocusCheckTimer_Tick(object? sender, EventArgs e)
        {
            var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            bool isAnyWindowActive = activeWindow != null;
            
            if (_wasApplicationActive && !isAnyWindowActive)
            {
                System.Diagnostics.Debug.WriteLine("[FOCUS CHECK] App lost focus - hiding overlays");
                
                if (!_isOverlayInteractionActive && _switchSavedActiveWpfOverlays == null)
                {
                    if (_mainViewModel.ActiveWpfOverlays.Count > 0 || (_overlayWindow != null && _overlayWindow.IsVisible))
                    {
                        _switchSavedActiveWpfOverlays = new List<TimelineClipBase>(_mainViewModel.ActiveWpfOverlays);
                        _switchWasOverlayVisible = _overlayWindow != null && _overlayWindow.IsVisible;

                        _mainViewModel.ActiveWpfOverlays.Clear();
                        if (_overlayWindow != null && _overlayWindow.IsVisible)
                        {
                            _overlayWindow.Hide();
                        }
                    }
                }
            }
            else if (!_wasApplicationActive && isAnyWindowActive)
            {
                System.Diagnostics.Debug.WriteLine("[FOCUS CHECK] App gained focus - restoring overlays");
                
                if (_switchSavedActiveWpfOverlays != null)
                {
                    _mainViewModel.ActiveWpfOverlays.Clear();
                    
                    foreach (var clip in _switchSavedActiveWpfOverlays)
                    {
                        _mainViewModel.ActiveWpfOverlays.Add(clip);
                    }
                    _switchSavedActiveWpfOverlays = null;
                }

                if (_overlayWindow != null && _switchWasOverlayVisible)
                {
                    _overlayWindow.Show();
                    _switchWasOverlayVisible = false;
                }
            }
            
            _wasApplicationActive = isAnyWindowActive;
        }

        private void DeactivationCheckTimer_Tick(object? sender, EventArgs e)
        {
            _deactivationCheckTimer.Stop();
            System.Diagnostics.Debug.WriteLine("[TIMER] Deactivation timer fired");
            
            if (_isOverlayInteractionActive)
            {
                System.Diagnostics.Debug.WriteLine("[TIMER] Overlay interaction active - not hiding");
                return;
            }
            
            bool isMainWindowActive = this.IsActive;
            bool isOwnedWindowActive = this.OwnedWindows.OfType<Window>().Any(w => w.IsActive);
            
            System.Diagnostics.Debug.WriteLine($"[TIMER] MainWindow active: {isMainWindowActive}, Owned window active: {isOwnedWindowActive}");
            
            if (isMainWindowActive || isOwnedWindowActive)
            {
                System.Diagnostics.Debug.WriteLine("[TIMER] Window is active again - temporary deactivation");
                _switchSavedActiveWpfOverlays = null;
                _switchWasOverlayVisible = false;
                return;
            }
            
            var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
            System.Diagnostics.Debug.WriteLine($"[TIMER] Active app window: {activeWindow?.GetType().Name ?? "null"}");
            
            if (activeWindow != null)
            {
                System.Diagnostics.Debug.WriteLine("[TIMER] App window is active - not hiding");
                _switchSavedActiveWpfOverlays = null;
                _switchWasOverlayVisible = false;
                return;
            }
            
            if (_switchSavedActiveWpfOverlays == null && (_mainViewModel.ActiveWpfOverlays.Count > 0 || (_overlayWindow != null && _overlayWindow.IsVisible)))
            {
                System.Diagnostics.Debug.WriteLine($"[TIMER] Hiding {_mainViewModel.ActiveWpfOverlays.Count} overlays");
                _switchSavedActiveWpfOverlays = new List<TimelineClipBase>(_mainViewModel.ActiveWpfOverlays);
                _switchWasOverlayVisible = _overlayWindow != null && _overlayWindow.IsVisible;

                _mainViewModel.ActiveWpfOverlays.Clear();
                if (_overlayWindow != null && _overlayWindow.IsVisible)
                {
                    _overlayWindow.Hide();
                }
            }
            else
            {
                System.Diagnostics.Debug.WriteLine("[TIMER] Already saved or no overlays to hide");
            }
        }

        private void MainWindow_Activated(object? sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[ACTIVATION] MainWindow activated");
                
                if (_deactivationCheckTimer.IsEnabled)
                {
                    System.Diagnostics.Debug.WriteLine("[ACTIVATION] Timer is running - stopping it (temporary deactivation)");
                    _deactivationCheckTimer.Stop();
                    _switchSavedActiveWpfOverlays = null;
                    _switchWasOverlayVisible = false;
                    return;
                }
                
                if (_switchSavedActiveWpfOverlays != null)
                {
                    System.Diagnostics.Debug.WriteLine($"[ACTIVATION] Restoring {_switchSavedActiveWpfOverlays.Count} overlays");
                    _mainViewModel.ActiveWpfOverlays.Clear();
                    
                    foreach (var clip in _switchSavedActiveWpfOverlays)
                    {
                        _mainViewModel.ActiveWpfOverlays.Add(clip);
                    }
                    _switchSavedActiveWpfOverlays = null;
                }

                if (_overlayWindow != null && _switchWasOverlayVisible)
                {
                    _overlayWindow.Show();
                    _switchWasOverlayVisible = false;
                }

                BringProgressWindowsToFront();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[ACTIVATION ERROR] {ex.Message}");
            }
        }

        private void MainWindow_Deactivated(object? sender, EventArgs e)
        {
            try
            {
                System.Diagnostics.Debug.WriteLine("[DEACTIVATION] MainWindow deactivated");
                System.Diagnostics.Debug.WriteLine($"[DEACTIVATION] _isOverlayInteractionActive: {_isOverlayInteractionActive}");
                
                if (_isOverlayInteractionActive)
                {
                    System.Diagnostics.Debug.WriteLine("[DEACTIVATION] Overlay interaction active - ignoring");
                    return;
                }
                
                var activeWindow = Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive);
                System.Diagnostics.Debug.WriteLine($"[DEACTIVATION] Active window: {activeWindow?.GetType().Name ?? "null"}");
                
                if (activeWindow is OverlayWindow)
                {
                    System.Diagnostics.Debug.WriteLine("[DEACTIVATION] OverlayWindow is active - ignoring");
                    return;
                }
                
                System.Diagnostics.Debug.WriteLine("[DEACTIVATION] Starting timer");
                _deactivationCheckTimer.Stop();
                _deactivationCheckTimer.Start();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[DEACTIVATION ERROR] {ex.Message}");
            }
        }

        private void BringProgressWindowsToFront()
        {
            foreach (Window ownedWindow in this.OwnedWindows)
            {
                if (ownedWindow is OverlayWindow)
                    continue;

                if (ownedWindow.IsLoaded && ownedWindow.IsVisible)
                {
                    var windowHandle = new WindowInteropHelper(ownedWindow).Handle;
                    if (windowHandle != IntPtr.Zero)
                    {
                        SetWindowPos(windowHandle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                    }
                }
            }
        }

        private void Menu_SubmenuOpened(object sender, RoutedEventArgs e)
        {
            _isMenuOpen = true;
            // 메뉴가 열릴 때 OverlayWindow를 완전히 숨김
            if (_overlayWindow != null && _overlayWindow.IsLoaded)
            {
                _overlayWindow.SetVisible(false);
            }
        }

        private void Menu_SubmenuClosed(object sender, RoutedEventArgs e)
        {
            _isMenuOpen = false;
            // 메뉴가 닫힐 때 OverlayWindow를 다시 표시
            if (_overlayWindow != null && _overlayWindow.IsLoaded)
            {
                _overlayWindow.SetVisible(true);
                BringOverlayToFront(); // Update overlay position immediately
            }
        }

        private void BringOverlayToFront()
        {
            // 메뉴가 열려있을 때는 OverlayWindow가 숨겨져 있으므로 아무것도 하지 않음
            if (_isMenuOpen || _overlayWindow == null || !_overlayWindow.IsLoaded || !this.IsActive)
                return;
                
            // OverlayWindow의 핸들을 가져와서 Z-Order를 최상위로 설정
            var overlayHandle = new WindowInteropHelper(_overlayWindow).Handle;
            _overlayWindow.SetTopmost(true);
            _overlayWindow.SetHitTestable(true);
            SetWindowPos(overlayHandle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }

        private void InitializePlayhead()
        {
            _playheadLine = new Line
            {
                Stroke = Brushes.Red,
                StrokeThickness = 2,
                Y1 = 0,
                Y2 = PlayheadCanvas.ActualHeight > 0 ? PlayheadCanvas.ActualHeight : 540
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

                        if (_mainViewModel.IsTimelinePlaying && !_mainViewModel.IsScrubbing)
                        {
                            EnsurePlayheadVisible(newX);
                        }
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
                
                clickedTimeSec = Math.Max(0, clickedTimeSec);

                _mainViewModel.SeekTimeline(clickedTimeSec, isScrubbing: false);

                double clampedX = Math.Max(0, position.X);
                _playheadLine.X1 = clampedX;
                _playheadLine.X2 = clampedX;
            }
        }

        private void PlayheadCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            PlayheadCanvas.CaptureMouse();
            UpdatePlayheadFromMouseEvent(e);
            e.Handled = true;
        }

        private void PlayheadCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (Mouse.Captured == PlayheadCanvas)
            {
                UpdatePlayheadFromMouseEvent(e);
                e.Handled = true;
            }
        }

        private void PlayheadCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (Mouse.Captured == PlayheadCanvas)
            {
                PlayheadCanvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        }

        private void TimelineRulerCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (Mouse.Captured == TimelineRulerCanvas)
            {
                UpdatePlayheadFromMouseEvent(e);
                e.Handled = true;
            }
        }

        private void TimelineRulerCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (Mouse.Captured == TimelineRulerCanvas)
            {
                TimelineRulerCanvas.ReleaseMouseCapture();
                e.Handled = true;
            }
        }


        private void TimelineRulerCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            TimelineRulerCanvas.CaptureMouse();
            UpdatePlayheadFromMouseEvent(e);
            e.Handled = true;
        }

        private void UpdatePlayheadFromMouseEvent(MouseEventArgs e)
        {
            if (Mouse.Captured != TimelineRulerCanvas && Mouse.Captured != PlayheadCanvas) return; // Only while captured
            if ((DateTime.Now - _lastDragUpdateTime).TotalMilliseconds < DRAG_UPDATE_THROTTLE_MS) return;
            _lastDragUpdateTime = DateTime.Now;

            IInputElement relativeTo = Mouse.Captured == TimelineRulerCanvas ? (IInputElement)TimelineRulerCanvas : (IInputElement)PlayheadCanvas;
            Point position = e.GetPosition(relativeTo);
            double clickedTimeSec = position.X / _mainViewModel.VideoEditor.PixelsPerSecond;
            
            clickedTimeSec = Math.Max(0, clickedTimeSec);
            
            _mainViewModel.CurrentTimelineTimeMs = (long)(clickedTimeSec * 1000);
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

        

        private void TimelineCanvas_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource == sender || e.OriginalSource == TimelineCanvas)
            {
                if (!Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.RightCtrl))
                {
                    foreach (var clip in _mainViewModel.VideoEditor.TimelineClips)
                    {
                        clip.IsSelected = false;
                    }
                    _mainViewModel.VideoEditor.SelectedClip = null;
                }
                
                _isSelectingWithRectangle = true;
                _selectionStartPoint = e.GetPosition(TimelineCanvas);
                
                if (Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl))
                {
                    _clipsBeforeSelection = _mainViewModel.VideoEditor.TimelineClips
                        .Where(c => c.IsSelected)
                        .ToList();
                }
                else
                {
                    _clipsBeforeSelection.Clear();
                }
                
                if (_selectionRectangle == null)
                {
                    _selectionRectangle = new Rectangle
                    {
                        Stroke = Brushes.DodgerBlue,
                        StrokeThickness = 2,
                        Fill = new SolidColorBrush(Color.FromArgb(50, 30, 144, 255)),
                        StrokeDashArray = new DoubleCollection { 4, 2 }
                    };
                }
                
                TimelineCanvas.CaptureMouse();
                e.Handled = true;
            }
        }

        private void TimelineCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (_mainViewModel.IsScrubbing && Mouse.Captured == TimelineRulerCanvas)
            {
                UpdatePlayheadFromMouseEvent(e);
                e.Handled = true;
                return;
            }

            if (_mainViewModel.VideoEditor.IsResizing)
            {
                _mainViewModel.VideoEditor.UpdateClipResize(e.GetPosition(TimelineCanvas));
                e.Handled = true;
                return;
            }
            
            if (_isSelectingWithRectangle && Mouse.Captured == TimelineCanvas)
            {
                Point currentPoint = e.GetPosition(TimelineCanvas);
                
                double left = Math.Min(_selectionStartPoint.X, currentPoint.X);
                double top = Math.Min(_selectionStartPoint.Y, currentPoint.Y);
                double width = Math.Abs(currentPoint.X - _selectionStartPoint.X);
                double height = Math.Abs(currentPoint.Y - _selectionStartPoint.Y);
                
                if (width > 5 || height > 5)
                {
                    Canvas.SetLeft(_selectionRectangle, left);
                    Canvas.SetTop(_selectionRectangle, top);
                    _selectionRectangle.Width = width;
                    _selectionRectangle.Height = height;
                    
                    if (!SelectionCanvas.Children.Contains(_selectionRectangle))
                    {
                        SelectionCanvas.Children.Add(_selectionRectangle);
                    }
                    
                    UpdateClipSelection(left, top, width, height);
                }
                
                e.Handled = true;
            }
        }
        
        private void UpdateClipSelection(double left, double top, double width, double height)
        {
            var selectionRect = new Rect(left, top, width, height);
            bool ctrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);
            
            foreach (var clip in _mainViewModel.VideoEditor.TimelineClips)
            {
                double clipLeft = clip.StartPosition * _mainViewModel.VideoEditor.PixelsPerSecond;
                double clipTop = clip.TrackIndex * 60;
                double clipWidth = clip.Width;
                double clipHeight = 50;
                
                var clipRect = new Rect(clipLeft, clipTop, clipWidth, clipHeight);
                
                bool intersects = selectionRect.IntersectsWith(clipRect);
                
                if (ctrlPressed)
                {
                    bool wasSelectedBefore = _clipsBeforeSelection.Contains(clip);
                    clip.IsSelected = wasSelectedBefore ? !intersects : intersects;
                }
                else
                {
                    clip.IsSelected = intersects;
                }
            }
            
            _mainViewModel.VideoEditor.SynchronizeSelectedClips();
        }

        private void TimelineCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_mainViewModel.IsScrubbing)
            {
                _mainViewModel.IsScrubbing = false;
                if (Mouse.Captured == TimelineRulerCanvas)
                {
                    TimelineRulerCanvas.ReleaseMouseCapture();
                }
                _mainViewModel.ResumePlaybackIfNeeded();
                e.Handled = true;
            }
            else if (_mainViewModel.VideoEditor.IsResizing)
            {
                _mainViewModel.VideoEditor.EndClipResize();
                (sender as UIElement)?.ReleaseMouseCapture();
                e.Handled = true;
            }
            else if (_isSelectingWithRectangle)
            {
                _isSelectingWithRectangle = false;
                
                if (_selectionRectangle != null && SelectionCanvas.Children.Contains(_selectionRectangle))
                {
                    SelectionCanvas.Children.Remove(_selectionRectangle);
                }
                
                _mainViewModel.VideoEditor.SynchronizeSelectedClips();
                
                _clipsBeforeSelection.Clear();
                
                if (Mouse.Captured == TimelineCanvas)
                {
                    TimelineCanvas.ReleaseMouseCapture();
                }
                
                e.Handled = true;
            }
        }

        private void TimelineResizeThumb_DragDelta(object sender, System.Windows.Controls.Primitives.DragDeltaEventArgs e)
        {
            double newWidth = TimelineRulerCanvas.Width + e.HorizontalChange;
            double pixelsPerSecond = _mainViewModel.VideoEditor.PixelsPerSecond;
    
            double minWidth = 10 * pixelsPerSecond;
            if (newWidth < minWidth)
            {
                newWidth = minWidth;
            }

            _currentTimelineDurationSec = newWidth / pixelsPerSecond;
    
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

        private void EnsurePlayheadVisible(double playheadX)
        {
            double viewportLeft = TimelineScrollViewer.HorizontalOffset;
            double viewportRight = viewportLeft + TimelineScrollViewer.ViewportWidth;

            const double edgePadding = 40.0;
            double targetLeft = playheadX - edgePadding;
            double targetRight = playheadX + edgePadding;

            if (playheadX < viewportLeft + edgePadding)
            {
                TimelineScrollViewer.ScrollToHorizontalOffset(Math.Max(0, targetLeft));
                RulerScrollViewer.ScrollToHorizontalOffset(TimelineScrollViewer.HorizontalOffset);
                DrawTimelineRuler();
            }
            else if (playheadX > viewportRight - edgePadding)
            {
                double newOffset = Math.Max(0, targetRight - TimelineScrollViewer.ViewportWidth);
                TimelineScrollViewer.ScrollToHorizontalOffset(newOffset);
                RulerScrollViewer.ScrollToHorizontalOffset(TimelineScrollViewer.HorizontalOffset);
                DrawTimelineRuler();
            }
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }
    }
}