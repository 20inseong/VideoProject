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
using System.Collections.ObjectModel;

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
					// 보조 레이어들도 시간 보정
					foreach (var layer in Layers)
					{
						if (layer?.MediaPlayer == null) continue;
						if (Math.Abs(layer.MediaPlayer.Time - value) > 80)
						{
							layer.MediaPlayer.Time = value;
						}
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
					if (MediaPlayer != null) MediaPlayer.Volume = _volume;
					foreach (var layer in Layers)
					{
						layer.MediaPlayer.Volume = _volume;
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
					if (MediaPlayer != null) MediaPlayer.SetRate(_playbackRate);
					foreach (var layer in Layers)
					{
						layer.MediaPlayer.SetRate(_playbackRate);
					}
				}
			}
		}

		public ICommand SetSpeed05Command { get; }
		public ICommand SetSpeed075Command { get; }
		public ICommand SetSpeed1Command { get; }
		public ICommand SetSpeed125Command { get; }
		public ICommand SetSpeed15Command { get; }
		public ICommand SetSpeed2Command { get; }
		public ICommand SetSpeed5Command { get; }
		public ICommand SetSpeed10Command { get; }
		public ICommand SetSpeed25Command { get; }

		public ObservableCollection<VideoLayerViewModel> Layers { get; } = new ObservableCollection<VideoLayerViewModel>();

		public VideoLayerViewModel AddLayer(string filePath, double? left = null, double? top = null, double? width = null, double? height = null, double opacity = 1.0, int zIndex = 0)
		{
			var layer = new VideoLayerViewModel(_libVLC, filePath)
			{
				Left = left ?? 0,
				Top = top ?? 0,
				Width = width ?? 640,
				Height = height ?? 360,
				Opacity = opacity,
				ZIndex = zIndex
			};
			// 마스터와 동기화
			layer.MediaPlayer.Volume = Volume;
			layer.MediaPlayer.SetRate(PlaybackRate);
			if (MediaPlayer.Media != null)
			{
				layer.MediaPlayer.Time = MediaPlayer.Time;
			}
			Layers.Add(layer);
			// 마스터가 재생 중이면 즉시 재생 시작
			if (MediaPlayer.IsPlaying)
			{
				layer.MediaPlayer.Play();
			}
			return layer;
		}

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
				UIDispatcher.BeginInvoke(() =>
				{
					IsPlaying = true;
					(PlayPauseCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
					(StopCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
				});
			};

			MediaPlayer.Paused += (s, e) =>
			{
				UIDispatcher.BeginInvoke(() =>
				{
					IsPlaying = false;
					(PlayPauseCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
					(StopCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
				});
			};

			MediaPlayer.Stopped += (s, e) =>
			{
				UIDispatcher.BeginInvoke(() =>
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
				foreach (var layer in Layers)
				{
					layer.MediaPlayer.Pause();
				}
			}
			else
			{
				MediaPlayer.Play();
				foreach (var layer in Layers)
				{
					layer.MediaPlayer.Play();
				}
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
			foreach (var layer in Layers)
			{
				layer.MediaPlayer.Stop();
			}
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

			foreach (var layer in Layers)
			{
				try
				{
					layer.MediaPlayer.Stop();
					layer.MediaPlayer.Dispose();
				}
				catch { }
			}

			if (_libVLC != null)
			{
				_libVLC.Dispose();
			}

			GC.SuppressFinalize(this);
		}
	}
}
