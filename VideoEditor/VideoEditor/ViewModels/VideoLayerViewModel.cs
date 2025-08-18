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

		public VideoLayerViewModel(LibVLC libVLC, string filePath)
		{
			MediaPlayer = new MediaPlayer(libVLC);
			var media = new Media(libVLC, new System.Uri(filePath));
			MediaPlayer.Media = media;
		}
	}
} 