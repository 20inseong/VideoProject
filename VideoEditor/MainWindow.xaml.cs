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
        private bool _needsZOrderUpdate = false;


        public MainWindow()
        {
            InitializeComponent();

            Common.UIDispatcher.Initialize();
            _mainViewModel = new MainViewModel(this);
            DataContext = _mainViewModel;

            _mainViewModel.ExportStarted += MainViewModel_ExportStarted;

            this.Loaded += MainWindow_Loaded;
            _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;

            _mainViewModel.VideoEditor.PropertyChanged += VideoEditor_PropertyChanged;
            
            // Monitor dragging state to update clipping more frequently
            _mainViewModel.VideoEditor.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(VideoEditorViewModel.IsDraggingClip))
                {
                    if (_mainViewModel.VideoEditor.IsDraggingClip)
                    {
                        // During drag, update more frequently (every 16ms ~= 60fps)
                        _videoClippingTimer.Interval = TimeSpan.FromMilliseconds(16);
                        _needsZOrderUpdate = true;
                    }
                    else
                    {
                        // Not dragging, slower update rate is fine
                        _videoClippingTimer.Interval = TimeSpan.FromMilliseconds(100);
                        // Force one final update when drag ends
                        ClipVideoViewsToPlayerHost();
                    }
                }
            };
            
            // Subscribe to Z-order change events
            _mainViewModel.VideoClipZOrderChanged += (s, e) =>
            {
                _needsZOrderUpdate = true;
            };
            
            // Subscribe to ActiveVideoClips changes to apply clipping and Z-order
            _mainViewModel.ActiveVideoClips.CollectionChanged += (s, e) => 
            {
                _needsZOrderUpdate = true; // Mark that Z-order needs update
                Dispatcher.BeginInvoke(new Action(() => 
                {
                    ClipVideoViewsToPlayerHost();
                }), DispatcherPriority.Loaded);
            };
            
            // Also clip when video clips properties change (position, size, etc.)
            _mainViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(MainViewModel.ActiveVideoClips))
                {
                    _needsZOrderUpdate = true;
                    Dispatcher.BeginInvoke(new Action(() => ClipVideoViewsToPlayerHost()), DispatcherPriority.Loaded);
                }
            };
            
            // Periodically check and apply video clipping (especially during drag operations)
            // But only update Z-order when needed
            _videoClippingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _videoClippingTimer.Tick += (s, e) =>
            {
                ClipVideoViewsToPlayerHost();
                // OverlayWindow를 주기적으로 최상위로 유지 (비디오 HwndHost가 위로 올라오는 것 방지)
                // 단, MainWindow가 활성화되어 있을 때만
                BringOverlayToFront();
            };
            _videoClippingTimer.Start();

            _overlayWindow = new OverlayWindow
            {
                DataContext = _mainViewModel
            };

            LocationChanged += UpdateOverlayPosition;
            SizeChanged += UpdateOverlayPosition;
            VideoPlayerHost.SizeChanged += UpdateOverlayPosition;
            
            // Monitor PreviewViewbox size changes
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

            TimelineScrollViewer.ScrollChanged += (s, e) =>
            {
                RulerScrollViewer.ScrollToHorizontalOffset(TimelineScrollViewer.HorizontalOffset);
                if (e.HorizontalChange != 0)
                {
                    // Draw immediately so ticks update without lag
                    DrawTimelineRuler();
                }
            };

            TimelineRulerCanvas.PreviewMouseLeftButtonDown += TimelineRulerCanvas_PreviewMouseLeftButtonDown;
            TimelineRulerCanvas.PreviewMouseMove += TimelineRulerCanvas_PreviewMouseMove;
            TimelineRulerCanvas.PreviewMouseLeftButtonUp += TimelineRulerCanvas_PreviewMouseLeftButtonUp;

            // Enable scrubbing via playhead area as well
            PlayheadCanvas.PreviewMouseLeftButtonDown += PlayheadCanvas_PreviewMouseLeftButtonDown;
            PlayheadCanvas.PreviewMouseMove += PlayheadCanvas_PreviewMouseMove;
            PlayheadCanvas.PreviewMouseLeftButtonUp += PlayheadCanvas_PreviewMouseLeftButtonUp;

            TimelineCanvas.PreviewMouseMove += TimelineCanvas_PreviewMouseMove;
            TimelineCanvas.PreviewMouseLeftButtonUp += TimelineCanvas_PreviewMouseLeftButtonUp;

            VideoPlayerHost.SizeChanged += (s, e) =>
            {
                // VideoPlayerHost is now fixed at 1920x1080 inside a Viewbox
                // So we always use these fixed dimensions
                _mainViewModel.PlayerHostWidth = 1920;
                _mainViewModel.PlayerHostHeight = 1080;
            };

            DrawTimelineRuler();
        }

        private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // TextBox와 같은 입력 컨트롤에 포커스가 있을 때는 키보드 이동을 막습니다.
            // 이를 통해 TextBox에서 방향키로 커서를 움직이는 기본 동작을 유지할 수 있습니다.
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

            //this.Close();
        }

        private void UpdateOverlayPosition(object? sender, EventArgs e)
        {
            // Find the PreviewViewbox element
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
                    
                    // Calculate scale factor (actual size / fixed size)
                    // Fixed size is 1920x1080, actual size is the Viewbox's scaled size
                    double scaleX = previewViewbox.ActualWidth / 1920.0;
                    double scaleY = previewViewbox.ActualHeight / 1080.0;
                    
                    // Apply scale to overlay window
                    _overlayWindow.SetScale(scaleX, scaleY);
                    
                    // OverlayWindow를 항상 최상위로 유지
                    BringOverlayToFront();
                    
                    // Apply clipping to video views to ensure they don't render outside VideoPlayerHost
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
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        private void ClipVideoViewsToPlayerHost()
        {
            // Find the PreviewViewbox element
            var previewViewbox = this.FindName("PreviewViewbox") as FrameworkElement;
            
            if (_mainViewModel?.ActiveVideoClips == null || previewViewbox == null)
                return;

            if (previewViewbox.ActualWidth <= 0 || previewViewbox.ActualHeight <= 0)
                return;

            // --- 여기부터 수정: DPI 스케일링 팩터 가져오기 ---
            var source = PresentationSource.FromVisual(this);
            // 창이 아직 완전히 로드되지 않았을 경우를 대비한 예외 처리
            if (source == null || source.CompositionTarget == null) return;

            // M11 = 수평 DPI 배율, M22 = 수직 DPI 배율
            var dpiX = source.CompositionTarget.TransformToDevice.M11;
            var dpiY = source.CompositionTarget.TransformToDevice.M22;

            // Get PreviewViewbox bounds in screen coordinates (WPF DIPs)
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
            // --- 수정 끝 ---

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

            // Sort by TrackIndex: higher TrackIndex should be behind (rendered first)
            // Lower TrackIndex should be in front (rendered last, on top)
            var sortedClips = hwndToClipMap.OrderBy(kvp => kvp.Value.TrackIndex).ToList();

            // Get the OverlayWindow handle to position video windows below it
            IntPtr overlayHwnd = IntPtr.Zero;
            if (_overlayWindow != null && _overlayWindow.IsLoaded)
            {
                overlayHwnd = new WindowInteropHelper(_overlayWindow).Handle;
            }

            // Apply Z-order from back to front
            IntPtr insertAfter = overlayHwnd != IntPtr.Zero ? overlayHwnd : HWND_TOP;
            
            for (int i = sortedClips.Count - 1; i >= 0; i--)
            {
                var kvp = sortedClips[i];
                IntPtr hwnd = kvp.Key;
                
                SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                insertAfter = hwnd;
            }
            
            // Ensure OverlayWindow stays on top
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

                    // Check if the HwndHost is completely outside VideoPlayerHost bounds
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
            // Hide window completely by setting empty region
            IntPtr emptyRgn = CreateRectRgn(0, 0, 0, 0);
            if (emptyRgn != IntPtr.Zero)
            {
                SetWindowRgn(parentHwnd, emptyRgn, true);
                
                // Also hide all child windows
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

        // Public method to force immediate clipping update (called from ClipAdorner during drag)
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
                //_mainViewModel.ExportFinished -= MainViewModel_ExportFinished;
                _mainViewModel.PropertyChanged -= MainViewModel_PropertyChanged;
                if (_mainViewModel.VideoEditor != null)
                {
                    _mainViewModel.VideoEditor.PropertyChanged -= VideoEditor_PropertyChanged;
                }
            }

            _mainViewModel = new MainViewModel(this);
            DataContext = _mainViewModel;

            _mainViewModel.ExportStarted += MainViewModel_ExportStarted;
            //_mainViewModel.ExportFinished += MainViewModel_ExportFinished;
            _mainViewModel.PropertyChanged += MainViewModel_PropertyChanged;
            _mainViewModel.VideoEditor.PropertyChanged += VideoEditor_PropertyChanged;

            //videoView.MediaPlayer = _mainViewModel.PlayerViewModel.MainVideoPlayer;
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

        //private void MainWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        //{
        //    if (e.OriginalSource is TextBox || e.OriginalSource is Slider)
        //    {
        //        return;
        //    }

        //    if (e.Key == Key.Left || e.Key == Key.Right)
        //    {
        //        _mainViewModel.VideoEditor.MoveSelectedClipsByKey(e.Key);
        //        e.Handled = true;
        //    }
        //}

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
                this.IsEnabled = true; // MainWindow를 다시 활성화합니다.
                this.Activate();       // MainWindow를 맨 앞으로 가져옵니다.
                _progressWindow = null;  // 참조를 정리합니다.
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

            if (openFileDialog.ShowDialog() == true)
            {
                // --- ▼ [추가] 유효성 검사 결과를 저장할 리스트 ▼ ---
                var invalidFiles = new List<string>();
                int addedCount = 0;

                // --- ▼ [추가] 작업 중임을 나타내는 커서 변경 ▼ ---
                this.Cursor = Cursors.Wait;

                foreach (string filePath in openFileDialog.FileNames)
                {
                    string extension = System.IO.Path.GetExtension(filePath).ToLowerInvariant();
                    bool isValid = true; // 기본적으로 유효하다고 가정

                    // 비디오 파일 확장자인 경우에만 유효성 검사 수행
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

                // --- ▼ [추가] 커서 복원 ▼ ---
                this.Cursor = Cursors.Arrow;

                // --- ▼ [추가] 손상된 파일이 있었을 경우 사용자에게 알림 ▼ ---
                if (invalidFiles.Any())
                {
                    string message = $"선택한 파일 중 다음 {invalidFiles.Count}개는 손상되었거나 지원하지 않는 형식으로, 목록에서 제외되었습니다:\n\n" +
                                     string.Join("\n", invalidFiles);
                    MessageBox.Show(this, message, "파일 추가 오류", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            if (e.Data.GetDataPresent("TimelineClips") && e.Data.GetData("TimelineClips") is List<TimelineClipBase> draggedClips)
            {
                e.Effects = DragDropEffects.Move;

                // 드래그 업데이트 간격 조절 (선택사항이지만 성능에 도움됨)
                if ((DateTime.Now - _lastDragUpdateTime).TotalMilliseconds < 20) // 20ms 간격
                {
                    e.Handled = true;
                    return;
                }
                _lastDragUpdateTime = DateTime.Now;

                Point position = e.GetPosition(TimelineCanvas);
                double deltaTime = (position.X - vm.VideoEditor.DragStartPoint.X) / vm.VideoEditor.PixelsPerSecond;
                int deltaTrack = (int)Math.Round((position.Y - vm.VideoEditor.DragStartPoint.Y) / 60.0);

                // 실시간 미리보기: 속성 변경 이벤트 폭주를 줄이기 위해 배치 업데이트
                foreach (var clip in draggedClips)
                {
                    if (vm.VideoEditor.DraggedClipsOriginalState.TryGetValue(clip, out var originalState))
                    {
                        double desiredStart = originalState.OriginalStart + deltaTime;
                        int desiredTrack = Math.Clamp(originalState.OriginalTrack + deltaTrack, 0, 4);
                        clip.StartPosition = Math.Max(0, desiredStart);
                        clip.TrackIndex = desiredTrack;
                    }
                }
                // 드래그 중에도 눈금/폭은 즉시 반영되도록 총 길이 바인딩은 이미 ViewModel에서 업데이트됨
                // 무거운 동기화는 드랍 완료 시에만 수행
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

            return (majorTick, majorTick / 5, @"ss\.ff");
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            _overlayWindow.Owner = this;
            _overlayWindow.Show();
            
            // OverlayWindow를 항상 비디오 HwndHost 위에 유지
            BringOverlayToFront();
            
            // Delay the initial update to ensure everything is laid out
            Dispatcher.BeginInvoke(new Action(() => 
            {
                UpdateOverlayPosition(null, null);
            }), DispatcherPriority.Loaded);

            InitializePlayhead();
            DrawTimelineRuler();
        }

        private void BringOverlayToFront()
        {
            // OverlayWindow의 핸들을 가져와서 Z-Order를 최상위로 설정
            // 하지만 MainWindow가 활성화되어 있을 때만 적용
            if (_overlayWindow != null && _overlayWindow.IsLoaded && this.IsActive)
            {
                var overlayHandle = new WindowInteropHelper(_overlayWindow).Handle;
                SetWindowPos(overlayHandle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
            }
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

                        // Keep playhead visible during playback (do not lock when paused/scrubbing)
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

                _mainViewModel.SeekTimeline(clickedTimeSec, isScrubbing: false);

                _playheadLine.X1 = position.X;
                _playheadLine.X2 = position.X;
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
                // Use property setter path which handles pause/resume safely during playback
                UpdatePlayheadFromMouseEvent(e);
                e.Handled = true;
            }
        }

        private void TimelineRulerCanvas_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (Mouse.Captured == TimelineRulerCanvas)
            {
                TimelineRulerCanvas.ReleaseMouseCapture();
                // Let scrubbing timer finalize seek and resume playback
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

        

        private void TimelineCanvas_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Scrub only when the ruler owns mouse capture
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
            }
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

        private void EnsurePlayheadVisible(double playheadX)
        {
            // visible range in content coordinates
            double viewportLeft = TimelineScrollViewer.HorizontalOffset;
            double viewportRight = viewportLeft + TimelineScrollViewer.ViewportWidth;

            // add margin so the head isn't glued to edge
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
    }
}