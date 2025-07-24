using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using System.Windows.Input;
using VideoEditor.Common;
using System.ComponentModel;

namespace VideoEditor.ViewModels
{
    public class PlayerViewModel : ViewModelBase, IDisposable
    {
        private readonly LibVLC _libVLC;
        public MediaPlayer MediaPlayer { get; }
        private bool _isPlaying;
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }

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
            set => SetProperty(ref _currentTime, value);
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
                    // 볼륨 값이 변경될 때 LibVLCSharp의 MediaPlayer 볼륨도 업데이트
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

            // 2. LibVLC 및 MediaPlayer 인스턴스 생성
            _libVLC = new LibVLC();
            MediaPlayer = new MediaPlayer(_libVLC);

            PlayPauseCommand = new RelayCommand(ExecutePlayPause);
            StopCommand = new RelayCommand(ExecuteStop);

            MediaPlayer.Playing += (s, e) => IsPlaying = true;
            MediaPlayer.Paused += (s, e) => IsPlaying = false;
            MediaPlayer.Stopped += (s, e) =>
            {
                IsPlaying = false;
                CurrentTime = 0;
            };
            MediaPlayer.EndReached += (s, e) =>
            {
                IsPlaying = false;
                MediaPlayer.Stop();
            };
            MediaPlayer.TimeChanged += (s, e) => CurrentTime = e.Time;
            MediaPlayer.LengthChanged += (s, e) => TotalDuration = e.Length;
            MediaPlayer.Volume = _volume;
        }

        public void LoadMedia(string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;

            if (MediaPlayer.Media != null)
            {
                MediaPlayer.Stop();
                MediaPlayer.Media.Dispose();
                MediaPlayer.Media = null;
            }
            var media = new Media(_libVLC, new Uri(filePath));
            MediaPlayer.Media = media;
            media.Dispose();

            (PlayPauseCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void ExecutePlayPause(object? parameter)
        {
            MediaPlayer.Play();
        }

        private void ExecuteStop(object? parameter)
        {
            MediaPlayer.Stop();
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
