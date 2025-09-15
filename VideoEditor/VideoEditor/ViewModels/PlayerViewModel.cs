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
        public MediaPlayer MainVideoPlayer { get; }
        public List<MediaPlayer> AudioOnlyPlayers { get; }
        private const int AUDIO_PLAYER_COUNT = 5;
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
                    if (MainVideoPlayer != null && Math.Abs(MainVideoPlayer.Time - value) > 50)
                    {
                        MainVideoPlayer.Time = value;
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
                    if (MainVideoPlayer != null)
                    {
                        MainVideoPlayer.Volume = _volume;
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
                    if (MainVideoPlayer != null)
                    {
                        try
                        {
                            MainVideoPlayer.SetRate(value);
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
            MainVideoPlayer = new MediaPlayer(_libVLC);

            AudioOnlyPlayers = new List<MediaPlayer>();
            for (int i = 0; i < AUDIO_PLAYER_COUNT; i++)
            {
                var audioPlayer = new MediaPlayer(_libVLC);
                AudioOnlyPlayers.Add(audioPlayer);
            }

            SetSpeed05Command = new RelayCommand<object>(_ => SetPlaybackRate(0.5f));
            SetSpeed075Command = new RelayCommand<object>(_ => SetPlaybackRate(0.75f));
            SetSpeed1Command = new RelayCommand<object>(_ => SetPlaybackRate(1.0f));
            SetSpeed125Command = new RelayCommand<object>(_ => SetPlaybackRate(1.25f));
            SetSpeed15Command = new RelayCommand<object>(_ => SetPlaybackRate(1.5f));
            SetSpeed2Command = new RelayCommand<object>(_ => SetPlaybackRate(2.0f));
            SetSpeed5Command = new RelayCommand<object>(_ => SetPlaybackRate(5.0f));
            SetSpeed10Command = new RelayCommand<object>(_ => SetPlaybackRate(10.0f));
            SetSpeed25Command = new RelayCommand<object>(_ => SetPlaybackRate(25.0f));

            VideoViewBackground = DefaultPlayerBackgroundBrush;

            PlayPauseCommand = new RelayCommand<object>(ExecutePlayPause, CanExecutePlayPause);
            StopCommand = new RelayCommand<object>(ExecuteStop, CanExecuteStop);

            MainVideoPlayer.Playing += (s, e) => UIDispatcher.Invoke(() => IsPlaying = true);
            MainVideoPlayer.Paused += (s, e) => UIDispatcher.Invoke(() => IsPlaying = false);
            MainVideoPlayer.Stopped += (s, e) => UIDispatcher.Invoke(() =>
            {
                IsPlaying = false;
                CurrentTime = 0;
            });
            MainVideoPlayer.EndReached += (s, e) => UIDispatcher.Invoke(() => IsPlaying = false);
            MainVideoPlayer.TimeChanged += (s, e) => {
                if (Math.Abs(_currentTime - e.Time) > 50)
                {
                    CurrentTime = e.Time;
                }
            };
            MainVideoPlayer.Volume = _volume;
        }

        public void PauseAllPlayers()
        {
            if (MainVideoPlayer.IsPlaying)
            {
                MainVideoPlayer.Pause();
            }
            foreach (var player in AudioOnlyPlayers)
            {
                if (player.IsPlaying)
                {
                    player.Pause();
                }
            }
        }

        public void ResumeAllPlayers()
        {
            if (MainVideoPlayer.CanPause)
            {
                MainVideoPlayer.Play();
            }

            foreach (var player in AudioOnlyPlayers)
            {
                if (!player.IsPlaying && player.Media != null)
                {
                    player.Play();
                }
            }
        }

        public MediaPlayer? GetAvailableAudioPlayer()
        {
            return AudioOnlyPlayers.FirstOrDefault(p => !p.IsPlaying);
        }

        public void StopAllAudioPlayers()
        {
            foreach (var player in AudioOnlyPlayers)
            {
                if (player.IsPlaying)
                {
                    player.Stop();
                }
                if (player.Media != null)
                {
                    player.Media = null;
                }
            }
        }

        public void LoadMedia(string filePath)
        {
            var newMediaUri = new Uri(filePath).AbsoluteUri;
            if (MainVideoPlayer.Media?.Mrl == newMediaUri) return;

            MainVideoPlayer.Media?.Dispose();
            var media = new Media(_libVLC, new Uri(filePath));
            MainVideoPlayer.Media = media;
            IsControlBarVisible = true;
            PlaybackRate = 1.0f;
            VideoViewBackground = EmptySpaceBackgroundBrush;
        }

        public void Play()
        {
            if (MainVideoPlayer.Media != null)
            {
                MainVideoPlayer.Play();
                VideoViewBackground = DefaultPlayerBackgroundBrush;
            }
        }

        public void Pause()
        {
            if (MainVideoPlayer.IsPlaying)
                MainVideoPlayer.Pause();
        }

        public void Stop()
        {
            MainVideoPlayer.Stop();
            StopAllAudioPlayers();
        }

        private void ExecutePlayPause(object? _)
        {
            if (MainVideoPlayer.IsPlaying)
            {
                Pause();
            }
            else
            {
                Play();
            }
        }

        public bool CanExecutePlayPause(object? _) => MainVideoPlayer.Media != null;

        private void ExecuteStop(object? _)
        {
            Stop();
        }

        public bool CanExecuteStop(object? _)
        {
            var state = MainVideoPlayer.State;
            return state == VLCState.Playing || state == VLCState.Paused;

        }

        private void SetPlaybackRate(float rate)
        {
            PlaybackRate = rate;
        }

        public void PlayMediaFrom(string filePath, long startTimeMs)
        {
            if (MainVideoPlayer.Media?.Mrl == new Uri(filePath).AbsoluteUri && MainVideoPlayer.IsPlaying)
            {
                return;
            }

            MainVideoPlayer.Stop();

            var media = new Media(_libVLC, new Uri(filePath));
            MainVideoPlayer.Media = media;
            MainVideoPlayer.Play();
            MainVideoPlayer.Time = startTimeMs;
        }

        public void Dispose()
        {
            //if (MainVideoPlayer != null)
            //{
            //    MainVideoPlayer.Stop();
            //    MainVideoPlayer.Dispose();
            //}

            //if (_libVLC != null)
            //{
            //    _libVLC.Dispose();
            //}

            //GC.SuppressFinalize(this);

            MainVideoPlayer?.Dispose();
            foreach (var player in AudioOnlyPlayers)
            {
                player?.Dispose();
            }
            _libVLC?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
