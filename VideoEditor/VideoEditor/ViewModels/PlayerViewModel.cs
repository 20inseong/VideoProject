using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using System.Windows.Input;
using VideoEditor.Common;
using Wpf.Ui.Input;
using WpfMedia = System.Windows.Media;

namespace VideoEditor.ViewModels
{
    public class PlayerViewModel : ViewModelBase, IDisposable
    {
        internal readonly LibVLC _libVLC;
        public MediaPlayer MediaPlayer { get; }
        private bool _isPlaying;
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }

        private static readonly WpfMedia.Brush EmptySpaceBackgroundBrush = new WpfMedia.SolidColorBrush(WpfMedia.Colors.Black); // 검은색으로 설정
        private static readonly WpfMedia.Brush DefaultPlayerBackgroundBrush = new WpfMedia.SolidColorBrush((WpfMedia.Color)WpfMedia.ColorConverter.ConvertFromString("#525252"));

        private WpfMedia.Brush _videoViewBackground;
        public WpfMedia.Brush VideoViewBackground
        {
            get => _videoViewBackground;
            set => SetProperty(ref _videoViewBackground, value);
        }

        private bool _isControlBarVisible;
        public bool IsControlBarVisible
        {
            get => _isControlBarVisible;
            set => SetProperty(ref _isControlBarVisible, value);
        }
        public bool IsPlaying
        {
            get => _isPlaying;
            set
            {
                if (SetProperty(ref _isPlaying, value))
                {
                    OnPropertyChanged(nameof(PlayPauseButtonContent));
                }
            }
        }

        public string PlayPauseButtonContent => IsPlaying ? "❚❚" : "▶";

        private long _currentTime;
        public long CurrentTime
        {
            get => _currentTime;
            set
            {
                if (SetProperty(ref _currentTime, value))
                {
                    if (MediaPlayer != null && Math.Abs(MediaPlayer.Time - value) > 50)
                    {
                        MediaPlayer.Time = value;
                    }
                }
            }
        }

        private long _totalDuration;
        public long TotalDuration
        {
            get => _totalDuration;
            set => SetProperty(ref _totalDuration, value);
        }

        private int _volume = 70;
        public int Volume
        {
            get => _volume;
            set
            {
                if (SetProperty(ref _volume, value))
                {
                    if (MediaPlayer != null)
                    {
                        MediaPlayer.Volume = _volume;
                    }
                }
            }
        }

        private float _playbackRate = 1.0f;
        public float PlaybackRate
        {
            get => _playbackRate;
            set
            {
                if (SetProperty(ref _playbackRate, value))
                {
                    // 배속 값이 변경될 때 LibVLCSharp의 MediaPlayer 배속도 업데이트
                    if (MediaPlayer != null)
                    {
                        try
                        {
                            MediaPlayer.SetRate(value);
                        }
                        catch (Exception ex)
                        {
                            // 일부 배속 값에서 오류가 발생할 수 있음
                            System.Diagnostics.Debug.WriteLine($"배속 설정 오류: {ex.Message}");
                        }
                    }

                    // PlaybackRateText 속성도 함께 업데이트
                    OnPropertyChanged(nameof(PlaybackRateText));
                }
            }
        }

        public string PlaybackRateText => $"{PlaybackRate:F2}x";

        public ICommand SetSpeed05Command { get; }
        public ICommand SetSpeed075Command { get; }
        public ICommand SetSpeed1Command { get; }
        public ICommand SetSpeed125Command { get; }
        public ICommand SetSpeed15Command { get; }
        public ICommand SetSpeed2Command { get; }
        public ICommand SetSpeed5Command { get; }
        public ICommand SetSpeed10Command { get; }
        public ICommand SetSpeed25Command { get; }

        public PlayerViewModel()
        {
            Core.Initialize();
            _libVLC = new LibVLC();
            MediaPlayer = new MediaPlayer(_libVLC);

            SetSpeed05Command = new RelayCommand<object>(_ => SetPlaybackRate(0.5f));
            SetSpeed075Command = new RelayCommand<object>(_ => SetPlaybackRate(0.75f));
            SetSpeed1Command = new RelayCommand<object>(_ => SetPlaybackRate(1.0f));
            SetSpeed125Command = new RelayCommand<object>(_ => SetPlaybackRate(1.25f));
            SetSpeed15Command = new RelayCommand<object>(_ => SetPlaybackRate(1.5f));
            SetSpeed2Command = new RelayCommand<object>(_ => SetPlaybackRate(2.0f));
            SetSpeed5Command = new RelayCommand<object>(_ => SetPlaybackRate(5.0f));
            SetSpeed10Command = new RelayCommand<object>(_ => SetPlaybackRate(10.0f));
            SetSpeed25Command = new RelayCommand<object>(_ => SetPlaybackRate(25.0f));

            //VideoViewBackground = new WpfMedia.SolidColorBrush((WpfMedia.Color)WpfMedia.ColorConverter.ConvertFromString("#525252"));

            VideoViewBackground = DefaultPlayerBackgroundBrush;

            PlayPauseCommand = new RelayCommand<object>(ExecutePlayPause, CanExecutePlayPause);
            StopCommand = new RelayCommand<object>(ExecuteStop, CanExecuteStop);

            MediaPlayer.Playing += (s, e) => UIDispatcher.Invoke(() => IsPlaying = true);
            MediaPlayer.Paused += (s, e) => UIDispatcher.Invoke(() => IsPlaying = false);
            MediaPlayer.Stopped += (s, e) => UIDispatcher.Invoke(() =>
            {
                IsPlaying = false;
                CurrentTime = 0;
            });
            MediaPlayer.EndReached += (s, e) => UIDispatcher.Invoke(() => IsPlaying = false);
            MediaPlayer.TimeChanged += (s, e) => {
                if (Math.Abs(_currentTime - e.Time) > 50)
                {
                    CurrentTime = e.Time;
                }
            };
            MediaPlayer.Volume = _volume;
        }

        public void LoadMedia(string filePath)
        {
            var newMediaUri = new Uri(filePath).AbsoluteUri;
            if (MediaPlayer.Media?.Mrl == newMediaUri) return;

            MediaPlayer.Media?.Dispose();
            var media = new Media(_libVLC, new Uri(filePath));
            MediaPlayer.Media = media;
            IsControlBarVisible = true;
            PlaybackRate = 1.0f;
            VideoViewBackground = EmptySpaceBackgroundBrush;
        }

        public void Play()
        {
            if (MediaPlayer.Media != null)
            {
                MediaPlayer.Play();
                VideoViewBackground = DefaultPlayerBackgroundBrush;
            }
        }

        public void Pause()
        {
            if (MediaPlayer.IsPlaying)
                MediaPlayer.Pause();
        }

        public void Stop()
        {
            MediaPlayer.Stop();
        }

        private void ExecutePlayPause(object? _)
        {
            if (MediaPlayer.IsPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        public bool CanExecutePlayPause(object? _) => MediaPlayer.Media != null;

        private void ExecuteStop(object? _)
        {
            Stop();
        }

        public bool CanExecuteStop(object? _)
        {
            var state = MediaPlayer.State;
            return state == VLCState.Playing || state == VLCState.Paused;

        }

        private void SetPlaybackRate(float rate)
        {
            PlaybackRate = rate;
        }

        public void PlayMediaFrom(string filePath, long startTimeMs)
        {
            if (MediaPlayer.Media?.Mrl == new Uri(filePath).AbsoluteUri && MediaPlayer.IsPlaying)
            {
                return;
            }

            MediaPlayer.Stop(); // 일단 정지

            var media = new Media(_libVLC, new Uri(filePath));
            MediaPlayer.Media = media;
            MediaPlayer.Play();
            MediaPlayer.Time = startTimeMs;
        }

        public void Dispose()
        {
            if (MediaPlayer != null)
            {
                MediaPlayer.Stop();
                MediaPlayer.Dispose();
            }

            if (_libVLC != null)
            {
                _libVLC.Dispose();
            }

            GC.SuppressFinalize(this);
        }
    }
}
