using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;
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

        private Point _dragStartPoint;
        private double _originalClipStartPosition;
        private int _originalClipTrackIndex;

        public ICommand DropOnTimelineCommand { get; }
        public ICommand ClipMouseDownCommand { get; }
        public ICommand ClipMouseMoveCommand { get; }

        //public VideoClip? CurrentlyPlayingClip
        //{
        //    get => _currentlyPlayingClip;
        //    set => SetProperty(ref _currentlyPlayingClip, value);
        //}


        public ObservableCollection<VideoClip> TimelineClips
        {
            get => _timelineClips;
            set => SetProperty(ref _timelineClips, value);
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

        public VideoEditorViewModel()
        {
            TimelineClips = new ObservableCollection<VideoClip>();
            Core.Initialize();
            _libVLC = new LibVLC();

            DropOnTimelineCommand = new RelayCommand<DragEventArgs>(ExecuteDropOnTimeline);

            ClipMouseDownCommand = new RelayCommand<MouseButtonEventArgs>(ExecuteClipMouseDown);
            ClipMouseMoveCommand = new RelayCommand<MouseEventArgs>(ExecuteClipMouseMove);
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
                    double deltaY = finalDropPosition.Y - _dragStartPoint.Y;

                    double deltaTime = deltaX / this.PixelsPerSecond;

                    int deltaTrack = (int)Math.Round(deltaY / 60.0);

                    double newStartPosition = _originalClipStartPosition + deltaTime;
                    int newTrackIndex = _originalClipTrackIndex + deltaTrack;

                    droppedClip.StartPosition = Math.Max(0, newStartPosition);
                    droppedClip.TrackIndex = Math.Clamp(newTrackIndex, 0, 4);

                    Console.WriteLine($"[Move LOG] '{droppedClip.Name}' 클립이 위치 {droppedClip.StartPosition}초, 트랙 {droppedClip.TrackIndex}로 이동됨");
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
                // --- 드롭한 위치에 클립을 추가하도록 수정 ---
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

        public void Dispose()
        {
            _libVLC?.Dispose();
        }

    }
}
