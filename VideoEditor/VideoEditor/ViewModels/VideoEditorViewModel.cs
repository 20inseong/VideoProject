using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Emgu.CV;
using LibVLCSharp.Shared;
using VideoEditor.Common;
using VideoEditor.Models;
using Wpf.Ui.Input;

namespace VideoEditor.ViewModels
{
    public class ClipAddedEventArgs : EventArgs
    {
        public string VideoPath { get; }
        public ClipAddedEventArgs(string videoPath)
        {
            VideoPath = videoPath;
        }
    }

    public class VideoEditorViewModel : ViewModelBase
    {
        private ObservableCollection<TimelineClipBase> _timelineClips;
        private double _pixelsPerSecond = 10.0;
        private LibVLC _libVLC;
        private readonly WaveformService _waveformService;
        public event EventHandler<ClipAddedEventArgs>? OnClipAdded;
        public event Action? ClipInteractionStarted;
        public event Action? ClipInteractionEnded;
        public TimelineClipBase? DraggedClip { get; private set; }
        private TimelineClipBase? _selectedClip;
        private TimelineClipBase? _copiedClip;

        private bool _isResizing = false;
        private TimelineClipBase? _resizingClip;
        private Point _resizeStartPoint;
        private double _originalClipDuration;

        public Point DragStartPoint { get; private set; }
        public double OriginalClipStartPosition { get; private set; }
        public int OriginalClipTrackIndex { get; private set; }

        public bool IsDraggingClip
        {
            get => _isDraggingClip;
            private set => SetProperty(ref _isDraggingClip, value);
        }
        private bool _isDraggingClip;



        public bool IsResizing => _isResizing;

        public ICommand DropOnTimelineCommand { get; }
        public ICommand ClipMouseDownCommand { get; }
        public ICommand ClipMouseMoveCommand { get; }
        public ICommand CopySelectedClipCommand { get; }
         public IRelayCommand<double> PasteClipCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand AddTextClipCommand { get; }
        public ICommand CreateSubtitlesFromTranscriptionCommand { get; }

        public ICommand SplitClipCommand { get; }
        public IRelayCommand GroupSelectedClipsCommand { get; }
        public IRelayCommand UngroupSelectedClipsCommand { get; }
        public IRelayCommand SeparateAudioCommand { get; }
        public RelayCommand<object> DeleteSelectedClipCommand { get; }

        public ObservableCollection<TimelineClipBase> TimelineClips
        {
            get => _timelineClips;
            set => SetProperty(ref _timelineClips, value);
        }

        public TimelineClipBase? SelectedClip
        {
            get => _selectedClip;
            set
            {
                if (SetProperty(ref _selectedClip, value))
                {
                    DeleteSelectedClipCommand.NotifyCanExecuteChanged();
                    (CopySelectedClipCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();

                    (CreateSubtitlesFromTranscriptionCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
                }
            }
        }

        public double PixelsPerSecond
        {
            get => _pixelsPerSecond;
            set
            {
                double clampedValue = Math.Clamp(value, 0.02, 200.0);
                if (SetProperty(ref _pixelsPerSecond, clampedValue))
                {
                    foreach (var clip in TimelineClips)
                    {
                        clip.UpdateWidth(_pixelsPerSecond);
                        clip.OnPropertyChanged(nameof(clip.StartPosition));
                    }
                }
            }
        }

        private VideoClip? _currentlyPlayingClip;
        public VideoClip? CurrentlyPlayingClip
        {
            get => _currentlyPlayingClip;
            set => SetProperty(ref _currentlyPlayingClip, value);
        }

        public VideoEditorViewModel()
        {
            TimelineClips = new ObservableCollection<TimelineClipBase>();
            TimelineClips.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (TimelineClipBase newClip in e.NewItems)
                    {
                        newClip.PropertyChanged += OnClipPropertyChanged;
                        newClip.UpdateWidth(PixelsPerSecond);
                    }
                }
                if (e.OldItems != null)
                {
                    foreach (TimelineClipBase oldClip in e.OldItems)
                    {
                        oldClip.PropertyChanged -= OnClipPropertyChanged;
                    }
                }
            };
            Core.Initialize();
            _libVLC = new LibVLC();
            _waveformService = new WaveformService();

            DropOnTimelineCommand = new RelayCommand<DragEventArgs>(ExecuteDropOnTimeline);
            ClipMouseDownCommand = new RelayCommand<MouseButtonEventArgs>(ExecuteClipMouseDown);
            ClipMouseMoveCommand = new RelayCommand<MouseEventArgs>(ExecuteClipMouseMove);
            DeleteSelectedClipCommand = new RelayCommand<object>(ExecuteDeleteSelectedClip, CanExecuteDeleteSelectedClip);

            CopySelectedClipCommand = new RelayCommand<object>(ExecuteCopySelectedClip, CanExecuteCopySelectedClip);
            PasteClipCommand = new RelayCommand<double>(ExecutePasteClip, CanExecutePasteClip);

            SplitClipCommand = new RelayCommand<double>(ExecuteSplitClip);

            ZoomInCommand = new RelayCommand<object>(_ => ZoomIn());
            ZoomOutCommand = new RelayCommand<object>(_ => ZoomOut());

            AddTextClipCommand = new RelayCommand<double>(ExecuteAddTextClip);

            CreateSubtitlesFromTranscriptionCommand = new RelayCommand<object>(ExecuteCreateSubtitlesFromTranscription, CanExecuteCreateSubtitlesFromTranscription);
        }

        private bool CanExecuteCreateSubtitlesFromTranscription(object? _)
        {
            if (SelectedClip == null) return false;

            if (SelectedClip is VideoClip vc && vc.Transcription.Any()) return true;
            if (SelectedClip is AudioClip ac && ac.Transcription.Any()) return true;

            return false;
        }

        private void ExecuteCreateSubtitlesFromTranscription(object? _)
        {
            // CanExecuteCreateSubtitlesFromTranscription 메서드 또한 object? 매개변수를 받도록 수정해야 합니다.
            // CanExecute..() -> CanExecute..(null)
            if (!CanExecuteCreateSubtitlesFromTranscription(null)) return;

            var transcriptionOwnerClip = SelectedClip;
            ObservableCollection<TranscriptionSegment>? segments = null;

            if (transcriptionOwnerClip is VideoClip vc) segments = vc.Transcription;
            else if (transcriptionOwnerClip is AudioClip ac) segments = ac.Transcription;

            if (segments == null) return;

            var result = MessageBox.Show(
                $"{segments.Count}개의 자막 클립을 타임라인에 추가하시겠습니까?",
                "자막 생성 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result == MessageBoxResult.No) return;

            foreach (var segment in segments)
            {
                double newTextClipStart = transcriptionOwnerClip.StartPosition + segment.Start.TotalSeconds;
                double newTextClipDuration = segment.Duration.TotalSeconds;

                if (newTextClipDuration < 0.1) newTextClipDuration = 0.1;

                int trackIndex = FindAvailableTrack(newTextClipStart, newTextClipDuration, 4, -1);

                var newTextClip = new TextClip
                {
                    Name = "자막",
                    Text = segment.Text.Trim(),
                    StartPosition = newTextClipStart,
                    Duration = newTextClipDuration,
                    TrackIndex = trackIndex,
                    RenderWidth = 600,
                    RenderHeight = 80,
                    X = 100,
                    Y = 400
                };

                TimelineClips.Add(newTextClip);
            }
            Debug.WriteLine($"[Subtitle Gen] {segments.Count}개의 자막 클립이 생성되었습니다.");
        }

        private void ExecuteAddTextClip(double creationTime)
        {
            const double defaultDuration = 5.0; // 자막 기본 길이 5초

            var newClip = new TextClip
            {
                Name = "새 자막",
                Text = "자막을 입력하세요",
                StartPosition = creationTime,
                Duration = defaultDuration,
                Width = defaultDuration * PixelsPerSecond,
                TrackIndex = FindAvailableTrack(creationTime, defaultDuration),
                X = 100, // Default X position
                Y = 100, // Default Y position
                RenderWidth = 200, // Default width for rendering
                RenderHeight = 50 // Default height for rendering
            };
            TimelineClips.Add(newClip);
        }

        private void ZoomIn()
        {
            PixelsPerSecond *= 1.6;
        }

        private void ZoomOut()
        {
            PixelsPerSecond /= 1.6;
        }

        private void ExecuteCopySelectedClip(object? _)
        {
            if (SelectedClip == null) return;

            _copiedClip = SelectedClip.Clone();
            PasteClipCommand.NotifyCanExecuteChanged();
            Console.WriteLine($"[Copy LOG] '{_copiedClip.Name}' 클립이 복사되었습니다.");
        }

        private bool CanExecuteCopySelectedClip(object? _)
        {
            return SelectedClip != null;
        }

        private void ExecutePasteClip(double pasteTime)
        {
            if (_copiedClip == null) return;

            var newClip = _copiedClip.Clone();
            newClip.StartPosition = pasteTime;

            newClip.TrackIndex = FindAvailableTrack(newClip.StartPosition, newClip.Duration);

            TimelineClips.Add(newClip);
            Debug.WriteLine($"[Paste LOG] '{newClip.Name}' 클립이 {pasteTime:F2}초, 트랙 {newClip.TrackIndex}에 붙여넣어졌습니다.");
        }

        private bool CanExecutePasteClip(double _)
        {
            return _copiedClip != null;
        }

        private int FindAvailableTrack(double startTime, double duration)
        {
            return FindAvailableTrack(startTime, duration, 0, 1);
        }

        private int FindAvailableTrack(double startTime, double duration, int startTrack, int direction)
        {
            if (direction > 0) // 위에서 아래로 (0 -> 4)
            {
                for (int track = startTrack; track <= 4; track++)
                {
                    bool isOccupied = TimelineClips.Any(c =>
                        c.TrackIndex == track &&
                        startTime < (c.StartPosition + c.Duration) &&
                        (startTime + duration) > c.StartPosition);
                    if (!isOccupied) return track;
                }
            }
            else // 아래에서 위로 (4 -> 0)
            {
                for (int track = startTrack; track >= 0; track--)
                {
                    bool isOccupied = TimelineClips.Any(c =>
                       c.TrackIndex == track &&
                       startTime < (c.StartPosition + c.Duration) &&
                       (startTime + duration) > c.StartPosition);
                    if (!isOccupied) return track;
                }
            }
            // 모든 트랙이 꽉 찼으면 원래 시작하려던 트랙을 반환
            return Math.Clamp(startTrack, 0, 4);
        }

        private void ExecuteDeleteSelectedClip(object? _)
        {
            if (SelectedClip != null)
            {
                TimelineClips.Remove(SelectedClip);
                SelectedClip = null;
                //Console.WriteLine("[Delete LOG] 클립이 삭제되었습니다.");
            }
        }

        private bool CanExecuteDeleteSelectedClip(object? _)
        {
            return SelectedClip != null;
        }

        private void ExecuteSplitClip(double currentTimelinePosition)
        {
            var originalClip = TimelineClips.FirstOrDefault(c =>
                c.StartPosition < currentTimelinePosition && (c.StartPosition + c.Duration) > currentTimelinePosition);

            if (originalClip == null) return;

            double originalDuration = originalClip.Duration;
            double splitPointInClip = currentTimelinePosition - originalClip.StartPosition;

            originalClip.Duration = splitPointInClip;
            originalClip.UpdateWidth(this.PixelsPerSecond);

            TimelineClipBase? newClip = null;

            switch (originalClip)
            {
                case VideoClip vc:
                    newClip = new VideoClip
                    {
                        Name = vc.Name + " (2)",
                        VideoPath = vc.VideoPath,
                        Thumbnail = vc.Thumbnail,
                        Category = vc.Category,
                        StartPosition = currentTimelinePosition,
                        Duration = originalDuration - splitPointInClip,
                        SourceStartTime = vc.SourceStartTime + splitPointInClip,
                        TrackIndex = vc.TrackIndex,
                        SourceWidth = vc.SourceWidth,
                        SourceHeight = vc.SourceHeight,
                        X = vc.X,
                        Y = vc.Y,
                        RenderWidth = vc.RenderWidth,
                        RenderHeight = vc.RenderHeight
                    };
                    break;

                case AudioClip ac:
                    newClip = new AudioClip
                    {
                        Name = ac.Name + " (2)",
                        AudioPath = ac.AudioPath,
                        StartPosition = currentTimelinePosition,
                        Duration = originalDuration - splitPointInClip,
                        SourceStartTime = ac.SourceStartTime + splitPointInClip,
                        TrackIndex = ac.TrackIndex
                    };
                    break;
                case ImageClip ic:
                    newClip = new ImageClip
                    {
                        Name = ic.Name + " (2)",
                        ImagePath = ic.ImagePath,
                        Thumbnail = ic.Thumbnail,
                        StartPosition = currentTimelinePosition,
                        Duration = originalDuration - splitPointInClip,
                        TrackIndex = ic.TrackIndex,
                        SourceWidth = ic.SourceWidth,
                        SourceHeight = ic.SourceHeight,
                        X = ic.X,
                        Y = ic.Y,
                        RenderWidth = ic.RenderWidth,
                        RenderHeight = ic.RenderHeight
                    };
                    break;
            }

            if (newClip != null)
            {
                newClip.UpdateWidth(this.PixelsPerSecond);

                int originalClipIndex = TimelineClips.IndexOf(originalClip);
                if (originalClipIndex != -1)
                {
                    TimelineClips.Insert(originalClipIndex + 1, newClip);
                }
                else
                {
                    TimelineClips.Add(newClip);
                }

                Debug.WriteLine($"[Split LOG] '{originalClip.Name}' 클립 자르기 완료. 새 클립 '{newClip.Name}' 생성됨.");
            }
        }

        private async Task GenerateWaveformForClipAsync(TimelineClipBase clip, string mediaPath)
        {
            if (string.IsNullOrEmpty(mediaPath)) return;

            clip.IsGeneratingWaveform = true;
            try
            {
                var waveformData = await _waveformService.GenerateWaveformDataAsync(mediaPath);
                clip.WaveformData = waveformData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to generate waveform for {mediaPath}: {ex.Message}");
            }
            finally
            {
                clip.IsGeneratingWaveform = false;
            }
        }

        private async void ExecuteDropOnTimeline(DragEventArgs? e)
        {
            if (e == null) return;

            if (e.Data.GetDataPresent("TimelineClip"))
            {
                if (e.Data.GetData("TimelineClip") is TimelineClipBase droppedClip && e.Source is FrameworkElement dropTarget)
                {
                    Point finalDropPosition = e.GetPosition(dropTarget);

                    double deltaX = finalDropPosition.X - DragStartPoint.X;
                    double deltaTime = deltaX / this.PixelsPerSecond;

                    int deltaTrack = (int)Math.Round((finalDropPosition.Y - DragStartPoint.Y) / 60.0);

                    double desiredStartPosition = OriginalClipStartPosition + deltaTime;
                    int newTrackIndex = Math.Clamp(OriginalClipTrackIndex + deltaTrack, 0, 4);

                    double adjustedStartPosition = AdjustClipPosition(droppedClip, desiredStartPosition, newTrackIndex);

                    droppedClip.StartPosition = adjustedStartPosition;
                    droppedClip.TrackIndex = newTrackIndex;

                    //Console.WriteLine($"[Move LOG] '{droppedClip.Name}' 클립이 위치 {droppedClip.StartPosition:F2}초, 트랙 {droppedClip.TrackIndex}로 이동됨");
                }
            }
            else if (e.Data.GetDataPresent("Myvideo"))
            {
                Myvideo droppedVideo = e.Data.GetData("Myvideo") as Myvideo;
                if (droppedVideo == null || !System.IO.File.Exists(droppedVideo.FullPath)) return;

                if (e.Source is FrameworkElement dropTarget)
                {
                    try
                    {
                        Point dropPosition = e.GetPosition(dropTarget);
                        double startTimeInSeconds = dropPosition.X / this.PixelsPerSecond;
                        int trackIndex = (int)(dropPosition.Y / 60.0);
                        trackIndex = Math.Clamp(trackIndex, 0, 4);
                        await AddMediaClipAsync(droppedVideo, startTimeInSeconds, trackIndex);
                    }
                    catch (Exception ex)
                    {
                        //Console.WriteLine($"클립 추가 중 오류 발생: {ex.Message}");
                    }
                }
            }
        }
        public async Task AddMediaClipAsync(Myvideo media, double dropPosition, int trackIndex)
        {
            string extension = Path.GetExtension(media.FullPath).ToLowerInvariant();
            TimelineClipBase? newClip = null;

            if (extension is ".mp4" or ".avi" or ".mov" or ".mkv")
            {
                newClip = await CreateVideoClipAsync(media, dropPosition, trackIndex);
            }
            else if (extension is ".mp3" or ".wav" or ".m4a" or ".aac")
            {
                newClip = await CreateAudioClipAsync(media, dropPosition, trackIndex);
            }
            else if (extension is ".jpg" or ".jpeg" or ".png" or ".bmp")
            {
                newClip = await CreateImageClipAsync(media, dropPosition, trackIndex);
            }

            if (newClip != null)
            {
                TimelineClips.Add(newClip);
                OnClipAdded?.Invoke(this, new ClipAddedEventArgs(media.FullPath));
                Debug.WriteLine($"[+] {newClip.GetType().Name} added: {newClip.Name}");
            }
        }

        private async Task<VideoClip?> CreateVideoClipAsync(Myvideo video, double position, int track)
        {
            double duration = 0;
            BitmapImage? thumbnail = null;

            int sourceWidth = 0;
            int sourceHeight = 0;

            try
            {
                using (var media = new Media(_libVLC, new Uri(video.FullPath)))
                {
                    await media.Parse(MediaParseOptions.ParseNetwork);
                    duration = media.Duration / 1000.0;
                }

                byte[] thumbnailBytes = await Task.Run(() =>
                {
                    try
                    {
                        using (var capture = new VideoCapture(video.FullPath))
                        {
                            sourceWidth = (int)capture.Get(Emgu.CV.CvEnum.CapProp.FrameWidth);
                            sourceHeight = (int)capture.Get(Emgu.CV.CvEnum.CapProp.FrameHeight);

                            int frameCount = (int)capture.Get(Emgu.CV.CvEnum.CapProp.FrameCount);
                            if (frameCount > 0)
                            {
                                capture.Set(Emgu.CV.CvEnum.CapProp.PosFrames, frameCount / 2);
                                using (var frame = new Mat())
                                {
                                    if (capture.Read(frame))
                                    {
                                        using (var bmp = frame.ToBitmap())
                                        using (var memory = new MemoryStream())
                                        {
                                            bmp.Save(memory, ImageFormat.Png);
                                            return memory.ToArray();
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"썸네일 생성 중 오류: {ex.Message}");
                    }
                    return Array.Empty<byte>();
                });

                if (thumbnailBytes.Length > 0)
                {
                    using (var memory = new MemoryStream(thumbnailBytes))
                    {
                        thumbnail = new BitmapImage();
                        thumbnail.BeginInit();
                        thumbnail.StreamSource = memory;
                        thumbnail.CacheOption = BitmapCacheOption.OnLoad;
                        thumbnail.EndInit();
                        thumbnail.Freeze();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"비디오 정보 로드 중 오류 발생: {ex.Message}");
                duration = 10;
                thumbnail = null;
            }

            var newClip = new VideoClip
            {
                Name = video.Title,
                VideoPath = video.FullPath,
                Duration = duration,
                StartPosition = position,
                Width = duration * PixelsPerSecond,
                Thumbnail = thumbnail,
                Category = video.Category,
                TrackIndex = track,

                SourceWidth = sourceWidth,
                SourceHeight = sourceHeight,
                
                // Add rendering properties for overlay display
                X = 0, // Initial X position
                Y = 0, // Initial Y position
                RenderWidth = sourceWidth, // Initial width for rendering
                RenderHeight = sourceHeight // Initial height for rendering
            };

            _ = GenerateWaveformForClipAsync(newClip, video.FullPath);

            newClip.UpdateWidth(this.PixelsPerSecond);
            return newClip;
        }

        private async Task<AudioClip?> CreateAudioClipAsync(Myvideo audio, double position, int track)
        {
            double duration = 0;
            try
            {
                using (var media = new Media(_libVLC, new Uri(audio.FullPath)))
                {
                    await media.Parse(MediaParseOptions.ParseNetwork);
                    duration = media.Duration / 1000.0;
                }

                if (duration <= 0) return null;

                var newClip = new AudioClip
                {
                    Name = audio.Title,
                    AudioPath = audio.FullPath,
                    StartPosition = position,
                    TrackIndex = track,
                    Duration = duration,
                    Width = duration * PixelsPerSecond
                };

                _ = GenerateWaveformForClipAsync(newClip, audio.FullPath);

                return newClip;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating audio clip: {ex.Message}");
                return null;
            }
        }

        private Task<ImageClip?> CreateImageClipAsync(Myvideo image, double position, int track)
        {
            try
            {
                //  이미지 파일을 로드하여 BitmapImage 객체 생성
                var thumbnail = new BitmapImage();
                thumbnail.BeginInit();
                thumbnail.UriSource = new Uri(image.FullPath);
                thumbnail.CacheOption = BitmapCacheOption.OnLoad;
                thumbnail.EndInit(); // 이미지 로드가 완료 및 정보확인 가능
                thumbnail.Freeze(); // 다른 스레드에서 접근할 수 있도록 프리즈

                const double defaultDuration = 5.0; // 이미지 클립의 기본 길이

                var clip = new ImageClip
                {
                    Name = image.Title,
                    ImagePath = image.FullPath,
                    Thumbnail = thumbnail,
                    StartPosition = position,
                    TrackIndex = track,
                    Duration = defaultDuration,

                    // 로드된 BitmapImage에서 직접 픽셀 너비와 높이를 읽어와 저장.
                    SourceWidth = thumbnail.PixelWidth,
                    SourceHeight = thumbnail.PixelHeight,

                    X = 0, // Initial X position
                    Y = 0, // Initial Y position
                    RenderWidth = thumbnail.PixelWidth, // Initial width for rendering
                    RenderHeight = thumbnail.PixelHeight // Initial height for rendering
                };

                clip.UpdateWidth(this.PixelsPerSecond); // 타임라인에서의 클립 너비 계산
                return Task.FromResult<ImageClip?>(clip);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"이미지 클립 생성 중 오류: {ex.Message}");
                return Task.FromResult<ImageClip?>(null);
            }
        }

        private void ExecuteClipMouseDown(MouseButtonEventArgs? e)
        {
            ClipInteractionStarted?.Invoke();

            if (e == null) return;

            if ((e.OriginalSource as FrameworkElement)?.Tag is "ResizeHandle")
            {
                return;
            }

            var itemsControl = (e.Source as FrameworkElement)?.FindAncestor<ItemsControl>();
            if (itemsControl != null)
            {
                itemsControl.Focus();
            }

            if ((e.Source as FrameworkElement)?.DataContext is TimelineClipBase clickedClip)
            {
                if (SelectedClip != null && SelectedClip != clickedClip)
                {
                    SelectedClip.IsSelected = false;
                }

                clickedClip.IsSelected = true;
                SelectedClip = clickedClip;

                DraggedClip = clickedClip;
                OriginalClipStartPosition = clickedClip.StartPosition;
                OriginalClipTrackIndex = clickedClip.TrackIndex;

                if (itemsControl != null)
                {
                    DragStartPoint = e.GetPosition(itemsControl);
                }
                IsDraggingClip = true;
            }
        }

        private void ExecuteClipMouseMove(MouseEventArgs? e)
        {
            if (_isResizing || e == null || DraggedClip == null || e.LeftButton != MouseButtonState.Pressed) return;

            DataObject dragData = new DataObject("TimelineClip", DraggedClip);
            DragDrop.DoDragDrop((DependencyObject)e.Source, dragData, DragDropEffects.Move);

            DraggedClip = null;
            IsDraggingClip = false;
            ClipInteractionEnded?.Invoke();
        }

        public void StartClipResize(TimelineClipBase clip, Point startPoint)
        {
            if (!(clip is ImageClip || clip is TextClip)) return;

            _isResizing = true;
            _resizingClip = clip;
            _resizeStartPoint = startPoint;
            _originalClipDuration = clip.Duration;

            if (SelectedClip != null && SelectedClip != clip)
            {
                SelectedClip.IsSelected = false;
            }
            clip.IsSelected = true;
            SelectedClip = clip;
        }

        public void UpdateClipResize(Point currentPoint)
        {
            if (!_isResizing || _resizingClip == null) return;

            double deltaX = currentPoint.X - _resizeStartPoint.X;
            double deltaTime = deltaX / PixelsPerSecond;

            // 최소 길이를 0.1초로 제한하여 클립이 사라지는 것을 방지
            double newDuration = Math.Max(0.1, _originalClipDuration + deltaTime);

            _resizingClip.Duration = newDuration;
            _resizingClip.UpdateWidth(PixelsPerSecond);
        }

        public void EndClipResize()
        {
            _isResizing = false;
            _resizingClip = null;
        }

        private double AdjustClipPosition(TimelineClipBase movingClip, double desiredStartPosition, int desiredTrackIndex)
        {
            double newStartPosition = Math.Max(0, desiredStartPosition);

            var otherClipsInTrack = TimelineClips
                .Where(c => c.TrackIndex == desiredTrackIndex && c.Id != movingClip.Id)
                .OrderBy(c => c.StartPosition)
                .ToList();

            foreach (var otherClip in otherClipsInTrack)
            {
                if (newStartPosition < (otherClip.StartPosition + otherClip.Duration) &&
                    (movingClip.StartPosition + movingClip.Duration) > otherClip.StartPosition) 
                {
                    newStartPosition = otherClip.StartPosition + otherClip.Duration;
                }
            }

            foreach (var otherClip in otherClipsInTrack)
            {
                if (otherClip.StartPosition < (newStartPosition + movingClip.Duration) &&
                    otherClip.StartPosition > newStartPosition)
                {
                    newStartPosition = otherClip.StartPosition - movingClip.Duration;
                }
            }

            return Math.Max(0, newStartPosition);
        }

        private void OnClipPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TimelineClipBase.Duration))
            {
                if (sender is TimelineClipBase clip)
                {
                    clip.UpdateWidth(PixelsPerSecond);
                }
            }

            if (sender == SelectedClip)
            {
                (CreateSubtitlesFromTranscriptionCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
            }
        }

        public void Dispose()
        {
            _libVLC?.Dispose();
        }

    }
}
