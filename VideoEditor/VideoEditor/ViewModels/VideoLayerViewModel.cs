using LibVLCSharp.Shared;
using VideoEditor.Common;
using System.Threading.Tasks;
using System.Diagnostics;

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

		// 원본 해상도 정보
		private uint _originalWidth;
		public uint OriginalWidth
		{
			get => _originalWidth;
			set => SetProperty(ref _originalWidth, value);
		}

		private uint _originalHeight;
		public uint OriginalHeight
		{
			get => _originalHeight;
			set => SetProperty(ref _originalHeight, value);
		}

		// 비율 유지하면서 크기 조정
		private double _aspectRatio = 16.0 / 9.0;
		public double AspectRatio
		{
			get => _aspectRatio;
			set => SetProperty(ref _aspectRatio, value);
		}

		public VideoLayerViewModel(LibVLC libVLC, string filePath)
		{
			MediaPlayer = new MediaPlayer(libVLC);
			var media = new Media(libVLC, filePath, FromType.FromPath);
			MediaPlayer.Media = media;
			
			// 비동기로 해상도 정보 가져오기
			_ = LoadVideoResolutionAsync();
		}

        private async Task LoadVideoResolutionAsync()
        {
            try
            {
                if (MediaPlayer.Media == null) return;

                // 미디어 파싱 (로컬 파일이면 ParseLocal, 네트워크면 ParseNetwork)
                await MediaPlayer.Media.Parse(MediaParseOptions.ParseLocal);

                // 파싱 후 트랙(들)에서 비디오 트랙 찾기
                var videoTrack = MediaPlayer.Media.Tracks?.FirstOrDefault(t => t.TrackType == TrackType.Video);
				foreach (var track in MediaPlayer.Media.Tracks)
				{
					switch (track.TrackType)
					{
						case TrackType.Audio:
							Debug.WriteLine("Audio track");
							Debug.WriteLine($"{nameof(track.Data.Audio.Channels)}: {track.Data.Audio.Channels}");
							Debug.WriteLine($"{nameof(track.Data.Audio.Rate)}: {track.Data.Audio.Rate}");
							break;
						case TrackType.Video:
							Debug.WriteLine("Video track");
                            //Debug.WriteLine($"{nameof(track.Data.Video.FrameRateNum)}: {track.Data.Video.FrameRateNum}");
                            //Debug.WriteLine($"{nameof(track.Data.Video.FrameRateDen)}: {track.Data.Video.FrameRateDen}");
                            //Debug.WriteLine($"{nameof(track.Data.Video.Height)}: {track.Data.Video.Height}");
                            //Debug.WriteLine($"{nameof(track.Data.Video.Width)}: {track.Data.Video.Width}");
                            uint w = track.Data.Video.Width;
                            uint h = track.Data.Video.Height;

							if (w > 0 && h > 0)
							{
								UIDispatcher.BeginInvoke(() =>
								{
									OriginalWidth = w;
									OriginalHeight = h;
									AspectRatio = (double)w / h;

									//ResizeToFit(3840,2160);
									const double maxWidth = 960;
									if (w > maxWidth)
									{
										Width = w / 4;
										Height = h / 4;
									}
									else
									{
										Width = w/4;
										Height = h/4;
									}
								});
							}
                            break;
                    }
				}
            }
            catch
            {
                // 파싱 실패 등 예외는 무시(로깅 권장)
            }
        }

        // 비율 유지하면서 크기 조정
        public void ResizeToFit(double maxWidth, double maxHeight)
		{
			if (AspectRatio <= 0) return;

			double scaleX = maxWidth / OriginalWidth;
			double scaleY = maxHeight / OriginalHeight;
			double scale = Math.Min(scaleX, scaleY);

			Width = OriginalWidth * scale;
			Height = OriginalHeight * scale;
		}
	}
} 