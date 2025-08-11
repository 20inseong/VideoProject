using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows;
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
        public ICommand DropOnTimelineCommand { get; }


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

        public VideoEditorViewModel()
        {
            TimelineClips = new ObservableCollection<VideoClip>();
            Core.Initialize();
            _libVLC = new LibVLC();

            DropOnTimelineCommand = new RelayCommand<DragEventArgs>(ExecuteDropOnTimeline);
        }

        private async void ExecuteDropOnTimeline(DragEventArgs e)
        {
            if (e.Data.GetDataPresent("Myvideo"))
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

                        Console.WriteLine($"[Drop LOG ViewModel] 계산된 TrackIndex: {trackIndex}");

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

                using (var capture = new VideoCapture(video.FullPath))
                {
                    int frameCount = (int)capture.Get(Emgu.CV.CvEnum.CapProp.FrameCount);
                    if (frameCount > 0)
                    {
                        capture.Set(Emgu.CV.CvEnum.CapProp.PosFrames, frameCount / 2);
                        Mat frame = new Mat();
                        if (capture.Read(frame))
                        {
                            using (var bmp = frame.ToBitmap())
                            using (var memory = new MemoryStream())
                            {
                                bmp.Save(memory, ImageFormat.Png);
                                memory.Position = 0;
                                thumbnail = new BitmapImage();
                                thumbnail.BeginInit();
                                thumbnail.StreamSource = memory;
                                thumbnail.CacheOption = BitmapCacheOption.OnLoad;
                                thumbnail.EndInit();
                                thumbnail.Freeze();
                                Console.WriteLine("[Debug] 썸네일 생성 성공!");
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"비디오 정보 로드 중 오류 발생: {ex.Message}");
                duration = 10;
                thumbnail = new BitmapImage();
            }

            var clipsOnTrack = this.TimelineClips.Where(c => c.TrackIndex == trackIndex);
            double newStartPosition = 0;

            if (clipsOnTrack.Any())
            {
                newStartPosition = clipsOnTrack.Max(c => c.StartPosition + c.Duration);
            }

            VideoClip newClip = new VideoClip
            {
                Name = video.Title,
                VideoPath = video.FullPath,
                Duration = duration,
                StartPosition = newStartPosition,
                Width = duration * this.PixelsPerSecond,
                Thumbnail = thumbnail,
                Category = video.Category,
                TrackIndex = trackIndex
            };

            TimelineClips.Add(newClip);
            Console.WriteLine($"클립 추가됨: {newClip.Name}, 시작 위치: {newClip.StartPosition}초, 길이: {newClip.Duration}초");

            OnClipAdded?.Invoke(this, new ClipAddedEventArgs(newClip.VideoPath));
        }

        public void Dispose()
        {
            _libVLC?.Dispose();
        }

    }
}
