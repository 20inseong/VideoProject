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
        public readonly LibVLC _libVLC;

        public List<MediaPlayer> VideoPlayers { get; }
        public List<MediaPlayer> AudioOnlyPlayers { get; }

        private const int VIDEO_PLAYER_COUNT = 5;
        private const int AUDIO_PLAYER_COUNT = 5;
        private bool _isPlaying;
        public ICommand PlayPauseCommand { get; }
        public ICommand StopCommand { get; }

        private static readonly WpfMedia.Brush EmptySpaceBackgroundBrush = new WpfMedia.SolidColorBrush(WpfMedia.Colors.Black);
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
                SetProperty(ref _currentTime, value);
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
            set => SetProperty(ref _volume, value);
        }

        private float _playbackRate = 1.0f;
        public float PlaybackRate
        {
            get => _playbackRate;
            set
            {
                if (SetProperty(ref _playbackRate, value))
                {
                    foreach (var player in VideoPlayers) player.SetRate(value);
                    foreach (var player in AudioOnlyPlayers) player.SetRate(value);
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

            VideoPlayers = new List<MediaPlayer>();
            for (int i = 0; i < VIDEO_PLAYER_COUNT; i++)
            {
                VideoPlayers.Add(new MediaPlayer(_libVLC));
            }

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

            PlayPauseCommand = new RelayCommand<object>(ExecutePlayPause);
            StopCommand = new RelayCommand<object>(ExecuteStop);

            var mainUiPlayer = VideoPlayers.FirstOrDefault();
            if (mainUiPlayer != null)
            {
                mainUiPlayer.Playing += (s, e) => UIDispatcher.Invoke(() => IsPlaying = true);
                mainUiPlayer.Paused += (s, e) => UIDispatcher.Invoke(() => IsPlaying = false);
                mainUiPlayer.Stopped += (s, e) => UIDispatcher.Invoke(() =>
                {
                    IsPlaying = false;
                    CurrentTime = 0;
                });
                mainUiPlayer.EndReached += (s, e) => UIDispatcher.Invoke(() => IsPlaying = false);
            }
        }

        private void ExecutePlayPause(object? obj) { }
        private void ExecuteStop(object? obj) { }

        public void PauseAllPlayers()
        {
            foreach (var player in VideoPlayers.Where(p => p.IsPlaying)) player.Pause();
            foreach (var player in AudioOnlyPlayers.Where(p => p.IsPlaying)) player.Pause();
        }

        public void ResumeAllPlayers()
        {
            foreach (var player in VideoPlayers.Where(p => p.Media != null && p.CanPause)) player.Play();
            foreach (var player in AudioOnlyPlayers.Where(p => p.Media != null && !p.IsPlaying)) player.Play();
        }

        public MediaPlayer? GetAvailableAudioPlayer()
        {
            return AudioOnlyPlayers.FirstOrDefault(p => p.Media == null);
        }

        public void Stop()
        {
            foreach (var player in VideoPlayers)
            {
                player.Stop();
                player.Media?.Dispose();
                player.Media = null;
            }
            foreach (var player in AudioOnlyPlayers)
            {
                player.Stop();
                player.Media?.Dispose();
                player.Media = null;
            }
        }

        public Media PrepareMedia(string path, double seekTimeInSeconds, bool videoOnly, bool audioOnly)
        {
            var options = new List<string>
            {
                $":start-time={seekTimeInSeconds.ToString(System.Globalization.CultureInfo.InvariantCulture)}"
            };

            if (videoOnly)
            {
                options.Add(":no-audio");
            }

            if (audioOnly)
            {
                options.Add(":no-video");
            }

            return new Media(_libVLC, new Uri(path), options.ToArray());
        }

        private void SetPlaybackRate(float rate)
        {
            PlaybackRate = rate;
        }

        public void Dispose()
        {
            foreach (var player in VideoPlayers) player?.Dispose();
            foreach (var player in AudioOnlyPlayers) player?.Dispose();
            _libVLC?.Dispose();
            GC.SuppressFinalize(this);
        }
    }
}
