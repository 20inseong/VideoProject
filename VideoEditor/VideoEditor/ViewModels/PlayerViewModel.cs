using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using System.Windows.Input;
using VideoEditor.Common;
using System.ComponentModel;
using Wpf.Ui.Input;

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

        // 배속 관련 속성 추가
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

        // 배속 표시용 문자열 속성
        public string PlaybackRateText => $"{PlaybackRate:F2}x";

        // 배속 프리셋 명령들
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

            PlayPauseCommand = new RelayCommand<object>(ExecutePlayPause, CanExecutePlayPause);
            StopCommand = new RelayCommand<object>(ExecuteStop, CanExecuteStop);

            // 배속 프리셋 명령들 초기화
            SetSpeed05Command = new RelayCommand<object>(_ => SetPlaybackRate(0.5f));
            SetSpeed075Command = new RelayCommand<object>(_ => SetPlaybackRate(0.75f));
            SetSpeed1Command = new RelayCommand<object>(_ => SetPlaybackRate(1.0f));
            SetSpeed125Command = new RelayCommand<object>(_ => SetPlaybackRate(1.25f));
            SetSpeed15Command = new RelayCommand<object>(_ => SetPlaybackRate(1.5f));
            SetSpeed2Command = new RelayCommand<object>(_ => SetPlaybackRate(2.0f));
            SetSpeed5Command = new RelayCommand<object>(_ => SetPlaybackRate(5.0f));
            SetSpeed10Command = new RelayCommand<object>(_ => SetPlaybackRate(10.0f));
            SetSpeed25Command = new RelayCommand<object>(_ => SetPlaybackRate(25.0f));

            MediaPlayer.Playing += (s, e) =>
            {
                UIDispatcher.Invoke(() =>
                {
                    IsPlaying = true;
                    (PlayPauseCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
                    (StopCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
                });
            };

            MediaPlayer.Paused += (s, e) =>
            {
                UIDispatcher.Invoke(() =>
                {
                    IsPlaying = false;
                    (PlayPauseCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
                    (StopCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
                });
            };

            MediaPlayer.Stopped += (s, e) =>
            {
                UIDispatcher.Invoke(() =>
                {
                    IsPlaying = false;
                    CurrentTime = 0;
                    (PlayPauseCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
                    (StopCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
                });
            };
            MediaPlayer.EndReached += (s, e) =>
            {
                IsPlaying = false;
                MediaPlayer.Stop();
            };
            MediaPlayer.TimeChanged += (s, e) => {
                if (Math.Abs(_currentTime - e.Time) > 50)
                {
                    CurrentTime = e.Time;
                }
            };
            MediaPlayer.LengthChanged += (s, e) => TotalDuration = e.Length;
            MediaPlayer.Volume = _volume;
        }

        // 배속 설정 메서드
        private void SetPlaybackRate(float rate)
        {
            PlaybackRate = rate;
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

            // 미디어 로드 후 기본 배속으로 설정
            PlaybackRate = 1.0f;

            IsControlBarVisible = true;

            (PlayPauseCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
            (StopCommand as RelayCommand<object>).NotifyCanExecuteChanged();
        }

        private void ExecutePlayPause(object? _)
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

        public bool CanExecutePlayPause(object? _)
        {
            return MediaPlayer.Media != null;
        }

        private void ExecuteStop(object? _)
        {
            MediaPlayer.Stop();
            CurrentTime = 0;
        }

        public bool CanExecuteStop(object? _)
        {
            //return MediaPlayer.Media != null && (MediaPlayer.IsPlaying || MediaPlayer.State == VLCState.Paused);
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
