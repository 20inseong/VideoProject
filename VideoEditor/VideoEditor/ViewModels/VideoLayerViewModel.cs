using LibVLCSharp.Shared;
using VideoEditor.Common;

namespace VideoEditor.ViewModels
{
	public class VideoLayerViewModel : ViewModelBase
	{
		public MediaPlayer MediaPlayer { get; }

		private double _opacity = 1.0;
		public double Opacity
		{
			get => _opacity;
			set => SetProperty(ref _opacity, value);
		}

		private int _zIndex = 0;
		public int ZIndex
		{
			get => _zIndex;
			set => SetProperty(ref _zIndex, value);
		}

		private double _left;
		public double Left
		{
			get => _left;
			set => SetProperty(ref _left, value);
		}

		private double _top;
		public double Top
		{
			get => _top;
			set => SetProperty(ref _top, value);
		}

		private double _width = 640;
		public double Width
		{
			get => _width;
			set => SetProperty(ref _width, value);
		}

		private double _height = 360;
		public double Height
		{
			get => _height;
			set => SetProperty(ref _height, value);
		}

		private bool _isSelected = false;
		public bool IsSelected
		{
			get => _isSelected;
			set => SetProperty(ref _isSelected, value);
		}

		private double _playbackRate = 1.0;
		public double PlaybackRate
		{
			get => _playbackRate;
			set
			{
				if (SetProperty(ref _playbackRate, value))
				{
					MediaPlayer.SetRate((float)value);
				}
			}
		}

		private double _scaleX = 1.0;
		public double ScaleX
		{
			get => _scaleX;
			set => SetProperty(ref _scaleX, value);
		}

		private double _scaleY = 1.0;
		public double ScaleY
		{
			get => _scaleY;
			set => SetProperty(ref _scaleY, value);
		}

		private double _rotation = 0.0;
		public double Rotation
		{
			get => _rotation;
			set => SetProperty(ref _rotation, value);
		}

		public VideoLayerViewModel(LibVLC libVLC, string filePath)
		{
			MediaPlayer = new MediaPlayer(libVLC);
			var media = new Media(libVLC, new System.Uri(filePath));
			MediaPlayer.Media = media;
		}
	}
} 