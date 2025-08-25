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
        public PlayerViewModel()
        {
            Core.Initialize();
            _libVLC = new LibVLC();
            MediaPlayer = new MediaPlayer(_libVLC);

            VideoViewBackground = new WpfMedia.SolidColorBrush((WpfMedia.Color)WpfMedia.ColorConverter.ConvertFromString("#525252"));

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
            //MediaPlayer.LengthChanged += (s, e) => TotalDuration = e.Length;
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
        }

        public void Play()
        {
            if (MediaPlayer.Media != null)
                MediaPlayer.Play();
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
