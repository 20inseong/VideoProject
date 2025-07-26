using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;
using VideoEditor.Models;
using LibVLCSharp.Shared;
using VideoEditor.Common;

namespace VideoEditor.ViewModels
{
    public class VideoEditorViewModel : ViewModelBase
    {
        private ObservableCollection<VideoClip> _timelineClips;
        private double _pixelsPerSecond = 100.0;
        private LibVLC _libVLC;

        public ObservableCollection<VideoClip> TimelineClips
        {
            get => _timelineClips;
            set => SetProperty(ref _timelineClips, value);
        }

        public double PixelsPerSecond
        {
            get => _pixelsPerSecond;
            set => SetProperty(ref _pixelsPerSecond, value);
        }

        public VideoEditorViewModel()
        {
            TimelineClips = new ObservableCollection<VideoClip>();
            Core.Initialize();
            _libVLC = new LibVLC();
        }

        public async void AddVideoClip(Myvideo video, double dropPosition)
        {
            // 비디오 파일의 길이와 썸네일을 가져오기
            double duration = 0;
            BitmapImage thumbnail = null;

            try
            {
                // LibVLCSharp을 사용하여 미디어 로드 및 정보 가져오기
                using (var media = new Media(_libVLC, new Uri(video.FullPath)))
                {
                    await media.Parse(MediaParseOptions.ParseNetwork); // 비디오 정보 파싱
                    duration = media.Duration / 1000.0; // 밀리초를 초로 변환

                    // 썸네일 생성 (간단한 예시, 실제로는 더 복잡한 로직 필요)
                    // LibVLCSharp으로 직접 썸네일을 추출하는 것은 WPF UI 스레드와 동기화가 필요하며 복잡합니다.
                    // 여기서는 임시로 플레이스홀더 썸네일을 사용합니다.
                    // 실제 구현에서는 MediaElement나 FFmpeg 등의 라이브러리를 사용하여 썸네일을 추출해야 합니다.
                    thumbnail = new BitmapImage(new Uri("pack://application:,,,/VideoEditor;component/Assets/placeholder_thumbnail.png")); // 예시 썸네일 경로
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"비디오 정보 로드 중 오류 발생: {ex.Message}");
                // 오류 발생 시 기본값 설정
                duration = 10; // 기본 10초
                thumbnail = new BitmapImage(); // 빈 썸네일
            }

                // 클립의 시작 위치 계산 (드롭 위치를 기준으로)
                // 드롭된 위치를 시간으로 변환하고, 클립의 시작 위치를 0초부터 시작하도록 조정
                double startTimeInSeconds = dropPosition / PixelsPerSecond;

                // 새 VideoClip 인스턴스 생성
            VideoClip newClip = new VideoClip
            {
                Name = video.Title,
                VideoPath = video.FullPath,
                Duration = duration,
                StartPosition = startTimeInSeconds, // 타임라인 상의 시작 시간
                Width = duration * PixelsPerSecond, // 타임라인 UI에서의 너비
                Thumbnail = thumbnail,
                Category = video.Category,
                TrackIndex = 0 // 일단 첫 번째 트랙에 추가 (나중에 여러 트랙 지원 가능)
            };

            TimelineClips.Add(newClip);
            Console.WriteLine($"클립 추가됨: {newClip.Name}, 시작 위치: {newClip.StartPosition}초, 길이: {newClip.Duration}초");
        }

             // LibVLC 인스턴스 정리
        public void Dispose()
        {
            _libVLC?.Dispose();
        }

    }
}
