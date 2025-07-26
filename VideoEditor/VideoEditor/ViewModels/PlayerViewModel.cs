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
                    //IsControlBarVisible = value;
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
                    // MediaPlayer.Time과 현재 CurrentTime 값이 다를 때만 MediaPlayer의 시간을 업데이트하여 무한 루프 방지
                    if (MediaPlayer != null && Math.Abs(MediaPlayer.Time - value) > 50) // 50ms 오차 허용
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
            MediaPlayer.TimeChanged += (s, e) => {
                // MediaPlayer에서 시간이 변경될 때 CurrentTime 업데이트 (슬라이더와 텍스트에 반영됨)
                if (Math.Abs(_currentTime - e.Time) > 50) // 50ms 오차 허용 (무한 루프 방지)
                {
                    CurrentTime = e.Time;
                }
            };
            MediaPlayer.LengthChanged += (s, e) => TotalDuration = e.Length;
            MediaPlayer.Volume = _volume;
        }

        public void LoadMedia(string filePath)
        {
            if (MediaPlayer.Media != null)
            {
                MediaPlayer.Stop();
                MediaPlayer.Media.Dispose();
                MediaPlayer.Media = null;
            }
            var media = new Media(_libVLC, new Uri(filePath));
            MediaPlayer.Media = media;

            IsControlBarVisible = true;

            (PlayPauseCommand as RelayCommand)?.RaiseCanExecuteChanged();
            (StopCommand as RelayCommand)?.RaiseCanExecuteChanged();
        }

        private void ExecutePlayPause(object? parameter)
        {
            if (MediaPlayer.IsPlaying)
            {
                MediaPlayer.Pause();
            }
            else
            {
                MediaPlayer.Play();
            }
        }

        public bool CanExecutePlayPause(object? parameter)
        {
            return MediaPlayer.Media != null;
        }

        private void ExecuteStop(object? parameter)
        {
            MediaPlayer.Stop();
        }virtual 

        public bool CanExecuteStop(object? parameter)
        {
            return MediaPlayer.Media != null && (MediaPlayer.IsPlaying || MediaPlayer.State == VLCState.Paused);
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
