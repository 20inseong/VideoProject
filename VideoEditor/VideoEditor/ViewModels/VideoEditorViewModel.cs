using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
        private ObservableCollection<VideoClip> _timelineClips;
        private double _pixelsPerSecond = 10.0;
        private LibVLC _libVLC;
        public event EventHandler<ClipAddedEventArgs>? OnClipAdded;
        private VideoClip? _draggedClip;
        private VideoClip? _selectedClip;

        private Point _dragStartPoint;
        private double _originalClipStartPosition;
        private int _originalClipTrackIndex;

        public ICommand DropOnTimelineCommand { get; }
        public ICommand ClipMouseDownCommand { get; }
        public ICommand ClipMouseMoveCommand { get; }
        public RelayCommand<object> DeleteSelectedClipCommand { get; }

        public ObservableCollection<VideoClip> TimelineClips
        {
            get => _timelineClips;
            set => SetProperty(ref _timelineClips, value);
        }

        public VideoClip? SelectedClip
        {
            get => _selectedClip;
            set
            {
                if (SetProperty(ref _selectedClip, value))
                {
                    DeleteSelectedClipCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public double PixelsPerSecond
        {
            get => _pixelsPerSecond;
            set => SetProperty(ref _pixelsPerSecond, value);
        }

        private VideoClip? _currentlyPlayingClip;
        public VideoClip? CurrentlyPlayingClip
        {
            get => _currentlyPlayingClip;
            set => SetProperty(ref _currentlyPlayingClip, value);
        }

        public ICommand SplitClipCommand { get; }

        public VideoEditorViewModel()
        {
            TimelineClips = new ObservableCollection<VideoClip>();
            Core.Initialize();
            _libVLC = new LibVLC();

            DropOnTimelineCommand = new RelayCommand<DragEventArgs>(ExecuteDropOnTimeline);
            ClipMouseDownCommand = new RelayCommand<MouseButtonEventArgs>(ExecuteClipMouseDown);
            ClipMouseMoveCommand = new RelayCommand<MouseEventArgs>(ExecuteClipMouseMove);
            DeleteSelectedClipCommand = new RelayCommand<object>(ExecuteDeleteSelectedClip, CanExecuteDeleteSelectedClip);

            SplitClipCommand = new RelayCommand<double>(ExecuteSplitClip);
        }

        private void ExecuteDeleteSelectedClip(object? _)
        {
            if (SelectedClip != null)
            {
                TimelineClips.Remove(SelectedClip);
                SelectedClip = null;
                Console.WriteLine("[Delete LOG] 클립이 삭제되었습니다.");
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
            double originalSourceStartTime = originalClip.SourceStartTime;
            double splitPointInClip = currentTimelinePosition - originalClip.StartPosition;

            originalClip.Duration = splitPointInClip;
            originalClip.UpdateWidth(this.PixelsPerSecond);

            var newClip = new VideoClip(originalClip)
            {
                Name = originalClip.Name + " (2)",
                StartPosition = currentTimelinePosition,
                Duration = originalDuration - splitPointInClip,

                SourceStartTime = originalSourceStartTime + splitPointInClip,
            };
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

            Console.WriteLine($"[Split LOG] 자르기 완료. '{newClip.Name}'는 원본 영상의 {newClip.SourceStartTime:F2}초부터 재생됩니다.");
        }

        private async void ExecuteDropOnTimeline(DragEventArgs? e)
        {
            if (e == null) return;

            if (e.Data.GetDataPresent("VideoClip"))
            {
                if (e.Data.GetData("VideoClip") is VideoClip droppedClip && e.Source is FrameworkElement dropTarget)
                {
                    Point finalDropPosition = e.GetPosition(dropTarget);

                    double deltaX = finalDropPosition.X - _dragStartPoint.X;
                    double deltaTime = deltaX / this.PixelsPerSecond;

                    int deltaTrack = (int)Math.Round((finalDropPosition.Y - _dragStartPoint.Y) / 60.0); // Y축 이동량 계산

                    double desiredStartPosition = _originalClipStartPosition + deltaTime;
                    int newTrackIndex = Math.Clamp(_originalClipTrackIndex + deltaTrack, 0, 4);

                    // ✅ 새 헬퍼 메서드를 사용하여 최종 StartPosition 계산
                    double adjustedStartPosition = AdjustClipPosition(droppedClip, desiredStartPosition, newTrackIndex);

                    droppedClip.StartPosition = adjustedStartPosition;
                    droppedClip.TrackIndex = newTrackIndex;

                    Console.WriteLine($"[Move LOG] '{droppedClip.Name}' 클립이 위치 {droppedClip.StartPosition:F2}초, 트랙 {droppedClip.TrackIndex}로 이동됨");
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
                        await AddVideoClip(droppedVideo, startTimeInSeconds, trackIndex);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"클립 추가 중 오류 발생: {ex.Message}");
                    }
                }
            }
        }
        public async Task AddVideoClip(Myvideo video, double dropPosition, int trackIndex)
        {
            double duration = 0;
            BitmapImage thumbnail = null;

            try
            {
                using (var media = new Media(_libVLC, new Uri(video.FullPath)))
                {
                    await media.Parse(MediaParseOptions.ParseNetwork);
                    duration = media.Duration / 1000.0;
                }
                Console.WriteLine($"[Debug] 비디오 길이 분석 완료: {duration}초");

                byte[] thumbnailBytes = await Task.Run(() =>
                {
                    try
                    {
                        using (var capture = new VideoCapture(video.FullPath))
                        {
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
                        Console.WriteLine($"썸네일 생성 중 오류: {ex.Message}");
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
                        thumbnail.Freeze(); // UI 스레드 외에서 생성했으므로 Freeze 필수
                        Console.WriteLine("[Debug] 썸네일 생성 성공!");
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"비디오 정보 로드 중 오류 발생: {ex.Message}");
                duration = 10; // 기본값
                thumbnail = new BitmapImage();
            }

            VideoClip newClip = new VideoClip
            {
                Name = video.Title,
                VideoPath = video.FullPath,
                Duration = duration,
                StartPosition = dropPosition,
                Width = duration * PixelsPerSecond,
                Thumbnail = thumbnail,
                Category = video.Category,
                TrackIndex = trackIndex
            };

            TimelineClips.Add(newClip);
            Console.WriteLine($"클립 추가됨: {newClip.Name}, 시작 위치: {newClip.StartPosition}초, 길이: {newClip.Duration}초");

            OnClipAdded?.Invoke(this, new ClipAddedEventArgs(newClip.VideoPath));
        }

        private void ExecuteClipMouseDown(MouseButtonEventArgs? e)
        {
            if (e == null) return;

            if ((e.Source as FrameworkElement)?.DataContext is VideoClip clickedClip)
            {
                if (SelectedClip != null && SelectedClip != clickedClip)
                {
                    SelectedClip.IsSelected = false;
                }

                clickedClip.IsSelected = true;
                SelectedClip = clickedClip;

                _draggedClip = clickedClip;
                _originalClipStartPosition = clickedClip.StartPosition;
                _originalClipTrackIndex = clickedClip.TrackIndex;

                var itemsControl = (e.Source as FrameworkElement).FindAncestor<ItemsControl>();
                if (itemsControl != null)
                {
                    _dragStartPoint = e.GetPosition(itemsControl);
                }
            }
        }

        private void ExecuteClipMouseMove(MouseEventArgs? e)
        {
            if (e == null || _draggedClip == null || e.LeftButton != MouseButtonState.Pressed) return;

            DataObject dragData = new DataObject("VideoClip", _draggedClip);
            DragDrop.DoDragDrop((DependencyObject)e.Source, dragData, DragDropEffects.Move);

            _draggedClip = null;
        }

        private double AdjustClipPosition(VideoClip movingClip, double desiredStartPosition, int desiredTrackIndex)
        {
            // 1. 타임라인 시작점보다 작아지지 않도록 보정
            double newStartPosition = Math.Max(0, desiredStartPosition);

            // 2. 같은 트랙 내의 다른 클립들과의 충돌 검사 및 스냅
            var otherClipsInTrack = TimelineClips
                .Where(c => c.TrackIndex == desiredTrackIndex && c.Id != movingClip.Id)
                .OrderBy(c => c.StartPosition)
                .ToList();

            // 앞쪽 클립에 대한 스냅/충돌 처리
            foreach (var otherClip in otherClipsInTrack)
            {
                // otherClip ---- movingClip
                // otherClip 의 끝나는 지점 (otherClip.StartPosition + otherClip.Duration)
                // movingClip 의 시작 지점 (newStartPosition)

                // 만약 movingClip이 otherClip의 끝나는 지점을 침범하려고 하면, otherClip 바로 뒤에 붙도록 조정
                // 즉, otherClip의 끝나는 지점 = movingClip의 시작 지점
                if (newStartPosition < (otherClip.StartPosition + otherClip.Duration) && // 다른 클립의 끝을 침범
                    (movingClip.StartPosition + movingClip.Duration) > otherClip.StartPosition) // 하지만 movingClip이 다른 클립을 완전히 지나친 건 아님
                {
                    // 충돌 발생: movingClip의 시작점을 otherClip의 끝나는 지점으로 스냅
                    newStartPosition = otherClip.StartPosition + otherClip.Duration;
                }
            }

            // 뒤쪽 클립에 대한 스냅/충돌 처리 (앞쪽 클립과의 충돌 처리 후 다시 검사)
            // movingClip ---- otherClip
            // movingClip의 끝나는 지점 (newStartPosition + movingClip.Duration)
            // otherClip의 시작 지점 (otherClip.StartPosition)
            foreach (var otherClip in otherClipsInTrack)
            {
                if (otherClip.StartPosition < (newStartPosition + movingClip.Duration) && // movingClip의 끝이 otherClip의 시작을 침범
                    otherClip.StartPosition > newStartPosition) // movingClip이 otherClip 이전에 시작하는 경우
                {
                    // 충돌 발생: movingClip의 끝이 otherClip의 시작에 스냅되도록 조정
                    newStartPosition = otherClip.StartPosition - movingClip.Duration;
                }
            }

            // 최종적으로 newStartPosition이 음수가 되지 않도록 다시 확인
            return Math.Max(0, newStartPosition);
        }

        public void Dispose()
        {
            _libVLC?.Dispose();
        }

    }
}
