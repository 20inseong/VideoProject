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
using System.Windows.Threading;

namespace VideoEditor.ViewModels
{
	public class PlayerViewModel : ViewModelBase, IDisposable
	{
		private readonly LibVLC _libVLC;
		public MediaPlayer MediaPlayer { get; }
		private MediaPlayer? _clockPlayer;
		public MediaPlayer? ClockPlayer => _clockPlayer;
		private readonly DispatcherTimer _clockTimer = new DispatcherTimer(DispatcherPriority.Render);
		private DateTime _lastTickUtc;
		public event EventHandler? ClockTimeChanged;
		public event EventHandler? ClockLengthChanged;
		public event EventHandler? Stopped;
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
					// 레이어들의 시간 보정
					foreach (var layer in Layers)
					{
						if (layer?.MediaPlayer == null) continue;
						if (Math.Abs(layer.MediaPlayer.Time - value) > 80)
						{
							layer.MediaPlayer.Time = value;
						}
					}
					ClockTimeChanged?.Invoke(this, EventArgs.Empty);
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
				Opacity = opacity,
				ZIndex = zIndex
			};
			
			// 크기가 지정되지 않았으면 해상도에 맞게 자동 설정 (비동기로 처리됨)
			if (width == null || height == null)
			{
				// 기본값 설정 (해상도 로드 후 자동으로 업데이트됨)
				layer.Width = 640;
				layer.Height = 360;
			}
			else
			{
				layer.Width = width.Value;
				layer.Height = height.Value;
			}
			
			// 동기화
			layer.MediaPlayer.Volume = Volume;
			layer.MediaPlayer.SetRate(PlaybackRate);
			layer.MediaPlayer.Time = CurrentTime;
			layer.MediaPlayer.LengthChanged += (s, e) =>
			{
				TotalDuration = Math.Max(TotalDuration, e.Length);
				ClockLengthChanged?.Invoke(this, EventArgs.Empty);
			};
			Layers.Add(layer);
			// 첫 레이어를 시계로 사용
			if (_clockPlayer == null) _clockPlayer = layer.MediaPlayer;
			// 현재 재생 상태면 즉시 재생
			if (IsPlaying)
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

			MediaPlayer.Volume = _volume;

			_clockTimer.Interval = TimeSpan.FromMilliseconds(16);
			_clockTimer.Tick += (s, e) =>
			{
				var now = DateTime.UtcNow;
				if (_lastTickUtc == default)
				{
					_lastTickUtc = now;
					return;
				}
				var deltaMs = (now - _lastTickUtc).TotalMilliseconds * PlaybackRate;
				_lastTickUtc = now;

				var next = _currentTime + (long)deltaMs;
				if (next > TotalDuration && TotalDuration > 0)
				{
					_clockTimer.Stop();
					IsPlaying = false;
					CurrentTime = TotalDuration;
					Stopped?.Invoke(this, EventArgs.Empty);
					return;
				}
				CurrentTime = next;
			};
		}

		// 배속 설정 메서드
		private void SetPlaybackRate(float rate)
		{
			PlaybackRate = rate;
		}

		public void LoadMedia(string filePath, bool disableVideoOutput = false)
		{
			// 소프트 클록 방식: 마스터 미디어 로드는 수행하지 않음. 컨트롤바만 활성화.
			IsControlBarVisible = true;
			(PlayPauseCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
			(StopCommand as RelayCommand<object>).NotifyCanExecuteChanged();
		}

		private void ExecutePlayPause(object? _)
		{
			if (IsPlaying)
			{
				foreach (var layer in Layers)
				{
					layer.MediaPlayer.Pause();
				}
				_clockTimer.Stop();
				IsPlaying = false;
			}
			else
			{
				foreach (var layer in Layers)
				{
					layer.MediaPlayer.Play();
				}
				_lastTickUtc = default;
				_clockTimer.Start();
				IsPlaying = true;
			}
		}

		public bool CanExecutePlayPause(object? _)
		{
			return Layers.Count > 0;
		}

		private void ExecuteStop(object? _)
		{
			CurrentTime = 0;
			foreach (var layer in Layers)
			{
				layer.MediaPlayer.Stop();
			}
			_clockTimer.Stop();
			IsPlaying = false;
			Stopped?.Invoke(this, EventArgs.Empty);
		}

		public bool CanExecuteStop(object? _)
		{
			return Layers.Any(l => l.MediaPlayer.State == VLCState.Playing || l.MediaPlayer.State == VLCState.Paused);

		}
		public void Dispose()
		{
			if (MediaPlayer != null)
			{
				try { MediaPlayer.Stop(); } catch { }
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
