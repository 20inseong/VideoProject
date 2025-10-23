using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Emgu.CV;
using LibVLCSharp.Shared;
using VideoEditor.Common;
using VideoEditor.Models;
using CommunityToolkit.Mvvm.Input;
using System.Windows.Media;

namespace VideoEditor.ViewModels
{
    public class ClipAddedEventArgs : EventArgs
    {
        public string VideoPath { get; }
        public ClipAddedEventArgs(string videoPath)
        {
            VideoPath = videoPath;
        }
    }

    public class VideoEditorViewModel : ViewModelBase
    {
        private ObservableCollection<TimelineClipBase> _timelineClips;
        private double _pixelsPerSecond = 10.0;
        private LibVLC _libVLC;
        private readonly WaveformService _waveformService;
        public event EventHandler<ClipAddedEventArgs>? OnClipAdded;
        public event Action? ClipInteractionStarted;
        public event Action? ClipInteractionEnded;

        private readonly List<TimelineClipBase> _selectedClips = new List<TimelineClipBase>();
        public Dictionary<TimelineClipBase, (double OriginalStart, int OriginalTrack)> DraggedClipsOriginalState { get; } = new Dictionary<TimelineClipBase, (double, int)>();

        private TimelineClipBase? _copiedClip;

        private bool _isResizing = false;
        private TimelineClipBase? _resizingClip;
        private Point _resizeStartPoint;
        private double _originalClipDuration;

        public Point DragStartPoint { get; private set; }
        public double OriginalClipStartPosition { get; private set; }
        public int OriginalClipTrackIndex { get; private set; }

        public bool IsDraggingClip
        {
            get => _isDraggingClip;
            private set => SetProperty(ref _isDraggingClip, value);
        }
        private bool _isDraggingClip;

        public double ZoomPercentage => PixelsPerSecond * 10.0;

        public bool IsResizing => _isResizing;

        public IRelayCommand<DragEventArgs> DropOnTimelineCommand { get; }
        public IRelayCommand<MouseButtonEventArgs> ClipMouseDownCommand { get; }
        public IRelayCommand<MouseEventArgs> ClipMouseMoveCommand { get; }
        public IRelayCommand CopySelectedClipCommand { get; }
        public IRelayCommand<double> PasteClipCommand { get; }
        public IRelayCommand ZoomInCommand { get; }
        public IRelayCommand ZoomOutCommand { get; }
        public IRelayCommand<double> AddTextClipCommand { get; }
        public IRelayCommand DeleteSelectedClipCommand { get; }
        public IRelayCommand<double> SplitClipCommand { get; }
        public IRelayCommand GroupSelectedClipsCommand { get; }
        public IRelayCommand UngroupSelectedClipsCommand { get; }
        public IRelayCommand SeparateAudioCommand { get; }

        public ObservableCollection<TimelineClipBase> TimelineClips
        {
            get => _timelineClips;
            set => SetProperty(ref _timelineClips, value);
        }

        private TimelineClipBase? _primarySelectedClip;
        public TimelineClipBase? SelectedClip
        {
            get => _primarySelectedClip;
            set
            {
                if (SetProperty(ref _primarySelectedClip, value))
                {
                    DeleteSelectedClipCommand.NotifyCanExecuteChanged();
                    CopySelectedClipCommand.NotifyCanExecuteChanged();
                    SplitClipCommand.NotifyCanExecuteChanged();
                    SeparateAudioCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public double PixelsPerSecond
        {
            get => _pixelsPerSecond;
            set
            {
                double clampedValue = Math.Clamp(value, 1.0, 100.0);
                if (SetProperty(ref _pixelsPerSecond, clampedValue))
                {
                    foreach (var clip in TimelineClips)
                    {
                        clip.UpdateWidth(_pixelsPerSecond);
                        clip.OnPropertyChanged(nameof(clip.StartPosition));
                    }
                    OnPropertyChanged(nameof(ZoomPercentage));
                }
            }
        }

        private VideoClip? _currentlyPlayingClip;
        public VideoClip? CurrentlyPlayingClip
        {
            get => _currentlyPlayingClip;
            set => SetProperty(ref _currentlyPlayingClip, value);
        }

        public VideoEditorViewModel()
        {
            TimelineClips = new ObservableCollection<TimelineClipBase>();
            Core.Initialize();
            _libVLC = new LibVLC();
            _waveformService = new WaveformService();

            DropOnTimelineCommand = new RelayCommand<DragEventArgs>(ExecuteDropOnTimeline);
            ClipMouseDownCommand = new RelayCommand<MouseButtonEventArgs>(ExecuteClipMouseDown);
            ClipMouseMoveCommand = new RelayCommand<MouseEventArgs>(ExecuteClipMouseMove);

            // [개선] 불필요한 object 파라미터 제거, CanExecute 메서드와 1:1 매칭
            DeleteSelectedClipCommand = new RelayCommand(ExecuteDeleteSelectedClip, CanExecuteDeleteSelectedClip);
            CopySelectedClipCommand = new RelayCommand(ExecuteCopySelectedClip, CanExecuteCopySelectedClip);

            PasteClipCommand = new RelayCommand<double>(ExecutePasteClip, CanExecutePasteClip);
            SplitClipCommand = new RelayCommand<double>(ExecuteSplitClip, CanExecuteSplitClip); // [추가] CanExecute 추가
            ZoomInCommand = new RelayCommand(ZoomIn);

            ZoomOutCommand = new RelayCommand(ZoomOut);
            AddTextClipCommand = new RelayCommand<double>(ExecuteAddTextClip);

            GroupSelectedClipsCommand = new RelayCommand(ExecuteGroupSelectedClips, CanExecuteGroupSelectedClips);
            UngroupSelectedClipsCommand = new RelayCommand(ExecuteUngroupSelectedClips, CanExecuteUngroupSelectedClips);

            SeparateAudioCommand = new RelayCommand(ExecuteSeparateAudio, CanExecuteSeparateAudio);
        }

        public void MoveSelectedClipsByKey(Key key)
        {
            // 1. 선택된 클립이 없으면 아무것도 하지 않습니다.
            if (!_selectedClips.Any()) return;

            // 2. 이동할 시간 단위를 결정합니다. (왼쪽: -1초, 오른쪽: +1초)
            double timeDelta = (key == Key.Left) ? -1.0 : 1.0;

            // 3. 실제로 이동해야 할 모든 클립 목록을 준비합니다.
            //    - 선택된 클립과, 그 클립들이 속한 그룹의 모든 멤버를 포함합니다.
            //    - HashSet을 사용하여 중복을 방지합니다.
            var clipsToMove = new HashSet<TimelineClipBase>();
            foreach (var selectedClip in _selectedClips)
            {
                if (selectedClip.GroupId.HasValue)
                {
                    // 그룹 멤버 전체를 추가합니다.
                    var groupMembers = TimelineClips.Where(c => c.GroupId == selectedClip.GroupId);
                    foreach (var member in groupMembers)
                    {
                        clipsToMove.Add(member);
                    }
                }
                else
                {
                    // 그룹이 없는 클립은 자신만 추가합니다.
                    clipsToMove.Add(selectedClip);
                }
            }

            // 4. 경계 및 충돌 검사를 수행합니다.

            // 4-1. 타임라인 시작(0초)보다 왼쪽으로 이동하는지 확인합니다.
            double minStartPosition = clipsToMove.Min(c => c.StartPosition);
            if (minStartPosition + timeDelta < 0)
            {
                // 0초에 "달라붙도록" 이동량을 조절합니다.
                timeDelta = -minStartPosition;
            }

            // 이동량이 0이면 더 이상 진행할 필요가 없습니다.
            if (Math.Abs(timeDelta) < 0.001) return;


            // 4-2. 다른 클립과 충돌하는지 확인합니다.
            var nonMovingClips = TimelineClips.Except(clipsToMove).ToList();
            bool collisionDetected = false;
            foreach (var clip in clipsToMove)
            {
                double newStart = clip.StartPosition + timeDelta;
                double newEnd = newStart + clip.Duration;

                // 같은 트랙에 있는 다른 클립과 겹치는지 확인합니다.
                if (nonMovingClips.Any(other =>
                    other.TrackIndex == clip.TrackIndex &&
                    newStart < (other.StartPosition + other.Duration) &&
                    newEnd > other.StartPosition))
                {
                    collisionDetected = true;
                    Debug.WriteLine($"[Move Collision] '{clip.Name}' 클립 이동 시 충돌 발생!");
                    break;
                }
            }
             
            // 충돌이 감지되면 이동을 취소합니다.
            if (collisionDetected) return;

            // 5. 모든 검사를 통과했으면, 클립들을 실제로 이동시킵니다.
            foreach (var clip in clipsToMove)
            {
                clip.StartPosition += timeDelta;
            }

            // 타임라인 변경 사항을 알립니다 (필요 시).
            OnTimelineChanged?.Invoke();
        }

        private bool CanExecuteSeparateAudio()
        {
            // 선택된 클립이 정확히 1개이고, 그것이 음소거되지 않은 'VideoClip'일 때만 활성화
            return SelectedClip is VideoClip vc && !vc.IsMuted && _selectedClips.Count == 1;
        }

        private void ExecuteSeparateAudio()
        {
            if (!CanExecuteSeparateAudio()) return;

            var originalVideoClip = (VideoClip)SelectedClip;

            // 1. 바로 아래 트랙에 공간이 있는지 확인
            int audioTrackIndex = originalVideoClip.TrackIndex + 1;
            if (audioTrackIndex > 4) // 최대 트랙 수 제한
            {
                MessageBox.Show("클립 바로 아래에 오디오를 추가할 트랙이 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            bool isTrackOccupied = TimelineClips.Any(c =>
               c.TrackIndex == audioTrackIndex &&
               originalVideoClip.StartPosition < (c.StartPosition + c.Duration) &&
               (originalVideoClip.StartPosition + originalVideoClip.Duration) > c.StartPosition
           );

            if (isTrackOccupied)
            {
                MessageBox.Show("클립 바로 아래 트랙의 해당 시간대에 다른 클립이 있어 오디오를 분리할 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. '영상 전용'으로 사용할 새로운 VideoClip 생성 (기존 클립을 복제하여 사용)
            var videoOnlyClip = originalVideoClip.Clone() as VideoClip;
            if (videoOnlyClip == null) return; // 복제 실패 시 중단

            videoOnlyClip.IsMuted = true; // 소리 끄기
            videoOnlyClip.Name = $"{originalVideoClip.Name} (영상)"; // 이름 변경

            // 3. '오디오 전용'으로 사용할 새로운 AudioClip 생성
            var audioOnlyClip = new AudioClip
            {
                Name = $"{originalVideoClip.Name} (오디오)",
                AudioPath = originalVideoClip.VideoPath,
                StartPosition = originalVideoClip.StartPosition,
                SourceStartTime = originalVideoClip.SourceStartTime,
                TrackIndex = audioTrackIndex, // 바로 아래 트랙에 배치
                Volume = originalVideoClip.Volume,

                SpeedRatio = originalVideoClip.SpeedRatio,
                Duration = originalVideoClip.Duration,
            };
            audioOnlyClip.UpdateWidth(this.PixelsPerSecond);


            // 4. 새로운 그룹 ID 생성 및 할당
            var newGroupId = Guid.NewGuid();
            videoOnlyClip.GroupId = newGroupId;
            audioOnlyClip.GroupId = newGroupId;

            // 5. ★★★ 핵심: 원본 클립을 제거하고, 새로 만든 두 개의 클립을 추가 ★★★
            TimelineClips.Remove(originalVideoClip);
            TimelineClips.Add(videoOnlyClip);
            TimelineClips.Add(audioOnlyClip);

            // 6. 새로 생성된 클립들을 선택 상태로 만듦
            foreach (var clip in _selectedClips) clip.IsSelected = false;
            _selectedClips.Clear();

            videoOnlyClip.IsSelected = true;
            audioOnlyClip.IsSelected = true;
            _selectedClips.Add(videoOnlyClip);
            _selectedClips.Add(audioOnlyClip);
            SelectedClip = videoOnlyClip; // 주 선택 클립 설정

            // 커맨드 상태 갱신
            SeparateAudioCommand.NotifyCanExecuteChanged();
            GroupSelectedClipsCommand.NotifyCanExecuteChanged();
            UngroupSelectedClipsCommand.NotifyCanExecuteChanged();
        }

        private void ExecuteGroupSelectedClips()
        {
            // 2개 이상의 클립이 선택되었을 때만 그룹화 진행
            if (_selectedClips.Count < 2) return;

            // 새로운 고유 ID를 생성
            var newGroupId = Guid.NewGuid();

            // 선택된 모든 클립에 동일한 그룹 ID를 할당
            foreach (var clip in _selectedClips)
            {
                clip.GroupId = newGroupId;
            }
            Debug.WriteLine($"[Group LOG] {_selectedClips.Count}개의 클립이 그룹(ID: {newGroupId})으로 묶였습니다.");
        }

        private bool CanExecuteGroupSelectedClips()
        {
            return _selectedClips.Count >= 2;
        }

        private void ExecuteUngroupSelectedClips()
        {
            var groupIdsToUngroup = _selectedClips
                .Where(c => c.GroupId.HasValue)
                .Select(c => c.GroupId.Value)
                .Distinct()
                .ToList();

            if (!groupIdsToUngroup.Any()) return;

            var clipsToUngroup = TimelineClips
                .Where(c => c.GroupId.HasValue && groupIdsToUngroup.Contains(c.GroupId.Value))
                .ToList();

            foreach (var clip in clipsToUngroup)
            {
                clip.GroupId = null;
            }
            Debug.WriteLine($"[Group LOG] {clipsToUngroup.Count}개의 클립이 포함된 {groupIdsToUngroup.Count}개의 그룹이 해제되었습니다.");
        }

        private bool CanExecuteUngroupSelectedClips()
        {
            return _selectedClips.Any(c => c.GroupId.HasValue);
        }

        private void ExecuteAddTextClip(double creationTime)
        {
            const double defaultDuration = 5.0;

            var newClip = new TextClip
            {
                Name = "새 자막",
                Text = "자막을 입력하세요",
                StartPosition = creationTime,
                Duration = defaultDuration,
                Width = defaultDuration * PixelsPerSecond,
                TrackIndex = FindAvailableTrack(creationTime, defaultDuration)
            };
            TimelineClips.Add(newClip);
        }

        private void ZoomIn() => PixelsPerSecond *= 1.25;
        private void ZoomOut() => PixelsPerSecond /= 1.25;

        private void ExecuteCopySelectedClip()
        {
            if (SelectedClip == null) return;
            _copiedClip = SelectedClip.Clone();
            PasteClipCommand.NotifyCanExecuteChanged();
            Debug.WriteLine($"[Copy LOG] '{_copiedClip.Name}' 클립이 복사되었습니다.");
        }

        private bool CanExecuteCopySelectedClip()
        {
            return SelectedClip != null;
        }

        private void ExecutePasteClip(double pasteTime)
        {
            if (_copiedClip == null) return;

            var newClip = _copiedClip.Clone();
            newClip.StartPosition = pasteTime;

            newClip.TrackIndex = FindAvailableTrack(newClip.StartPosition, newClip.Duration);

            TimelineClips.Add(newClip);
            Debug.WriteLine($"[Paste LOG] '{newClip.Name}' 클립이 {pasteTime:F2}초, 트랙 {newClip.TrackIndex}에 붙여넣어졌습니다.");
        }

        private bool CanExecutePasteClip(double _)
        {
            return _copiedClip != null;
        }

        private int FindAvailableTrack(double startTime, double duration)
        {
            for (int track = 0; track <= 4; track++)
            {
                bool isOccupied = TimelineClips.Any(c =>
                    c.TrackIndex == track &&
                    startTime < (c.StartPosition + c.Duration) &&
                    (startTime + duration) > c.StartPosition
                );

                if (!isOccupied)
                {
                    return track;
                }
            }
            return 0;
        }

        private void ExecuteDeleteSelectedClip()
        {
            if (SelectedClip != null)
            {
                TimelineClips.Remove(SelectedClip);
                SelectedClip = null;
                //Console.WriteLine("[Delete LOG] 클립이 삭제되었습니다.");
            }
        }

        private bool CanExecuteDeleteSelectedClip()
        {
            return _selectedClips.Any();
        }

        private void ExecuteSplitClip(double currentTimelinePosition)
        {
            var originalClip = SelectedClip;
            if (originalClip == null) return;

            if (!(currentTimelinePosition > originalClip.StartPosition && currentTimelinePosition < originalClip.StartPosition + originalClip.Duration))
                return;

            // 계산에 필요한 값들을 미리 저장합니다.
            double originalEndPosition = originalClip.StartPosition + originalClip.Duration;
            double splitPointInClip = currentTimelinePosition - originalClip.StartPosition;
            double splitPointInMedia = splitPointInClip * originalClip.SpeedRatio;

            // 깨끗한 원본을 먼저 복제합니다.
            TimelineClipBase newClip = originalClip.Clone();

            // 원본 클립(첫 번째 조각)을 수정합니다.
            originalClip.Duration = splitPointInClip;
            originalClip.UpdateWidth(this.PixelsPerSecond);

            // 복제된 클립(두 번째 조각)을 수정합니다.
            newClip.StartPosition = currentTimelinePosition;
            newClip.Duration = originalEndPosition - currentTimelinePosition;
            newClip.UpdateWidth(this.PixelsPerSecond);

            // [핵심 수정]
            // originalClip의 실제 타입을 확인하고, 안전하게 형 변환하여 SourceStartTime에 접근합니다.
            if (originalClip is VideoClip originalVideoClip && newClip is VideoClip newVideoClip)
            {
                // 이제 originalVideoClip은 VideoClip 타입이므로 SourceStartTime에 접근 가능합니다.
                newVideoClip.SourceStartTime = originalVideoClip.SourceStartTime + splitPointInMedia;
            }
            else if (originalClip is AudioClip originalAudioClip && newClip is AudioClip newAudioClip)
            {
                // AudioClip도 마찬가지로 안전하게 형 변환 후 접근합니다.
                newAudioClip.SourceStartTime = originalAudioClip.SourceStartTime + splitPointInMedia;
            }

            TimelineClips.Add(newClip);
        }

        private bool CanExecuteSplitClip(double currentTimelinePosition)
        {
            if (SelectedClip == null || _selectedClips.Count != 1) return false;

            return currentTimelinePosition > SelectedClip.StartPosition && currentTimelinePosition < SelectedClip.StartPosition + SelectedClip.Duration;
        }

        private async Task GenerateWaveformForClipAsync(TimelineClipBase clip, string mediaPath)
        {
            if (string.IsNullOrEmpty(mediaPath)) return;

            clip.IsGeneratingWaveform = true;
            try
            {
                var waveformData = await _waveformService.GenerateWaveformDataAsync(mediaPath);
                clip.WaveformData = waveformData;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to generate waveform for {mediaPath}: {ex.Message}");
            }
            finally
            {
                clip.IsGeneratingWaveform = false;
            }
        }

        private void ExecuteDropOnTimeline(DragEventArgs? e)
        {
            if (e == null) return;

            // 1. [수정] 올바른 데이터 키("TimelineClips")로 확인하고, List<TimelineClipBase> 형식으로 데이터를 받습니다.
            if (e.Data.GetDataPresent("TimelineClips") &&
                e.Data.GetData("TimelineClips") is List<TimelineClipBase> droppedClips &&
                e.Source is FrameworkElement dropTarget)
            {
                // The DragOver event now handles all position validation and previewing.
                // The Drop event simply finalizes the operation. Since the clip properties
                // are already updated for the preview, we just need to ensure the drag state is cleared.
                // The actual positioning is implicitly complete.
            }
            else if (e.Data.GetDataPresent("Myvideo"))
            {
                // (파일을 직접 드롭하는 이 부분은 기존 코드와 동일하게 유지합니다)
                Myvideo droppedVideo = e.Data.GetData("Myvideo") as Myvideo;
                if (droppedVideo == null || !File.Exists(droppedVideo.FullPath)) return;

                if (e.Source is FrameworkElement dropTargetForFile)
                {
                    try
                    {
                        Point dropPosition = e.GetPosition(dropTargetForFile);
                        double startTimeInSeconds = dropPosition.X / this.PixelsPerSecond;
                        int trackIndex = Math.Clamp((int)(dropPosition.Y / 60.0), 0, 4);
                        _ = AddMediaClipAsync(droppedVideo, startTimeInSeconds, trackIndex);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"클립 추가 중 오류 발생: {ex.Message}");
                    }
                }
            }
        }
        public async Task AddMediaClipAsync(Myvideo media, double dropPosition, int trackIndex)
        {
            Debug.WriteLine($"[AddMediaClipAsync] Received call for '{media.Title}'.");
            Debug.WriteLine($"    -> Received dropPosition: {dropPosition:F2}");
            Debug.WriteLine($"    -> Received trackIndex: {trackIndex}");

            string extension = Path.GetExtension(media.FullPath).ToLowerInvariant();
            TimelineClipBase? newClip = null;

            if (extension is ".mp4" or ".avi" or ".mov" or ".mkv")
            {
                newClip = await CreateVideoClipAsync(media, dropPosition, trackIndex);
            }
            else if (extension is ".mp3" or ".wav" or ".m4a" or ".aac")
            {
                newClip = await CreateAudioClipAsync(media, dropPosition, trackIndex);
            }
            else if (extension is ".jpg" or ".jpeg" or ".png" or ".bmp")
            {
                newClip = await CreateImageClipAsync(media, dropPosition, trackIndex);
            }

            if (newClip != null)
            {
                TimelineClips.Add(newClip);
                OnClipAdded?.Invoke(this, new ClipAddedEventArgs(media.FullPath));
                Debug.WriteLine($"[+] {newClip.GetType().Name} added: {newClip.Name}");
            }
        }

        private async Task<VideoClip?> CreateVideoClipAsync(Myvideo video, double position, int track)
        {
            double duration = 0;
            BitmapImage? thumbnail = null;

            int sourceWidth = 0;
            int sourceHeight = 0;

            try
            {
                using (var media = new Media(_libVLC, new Uri(video.FullPath)))
                {
                    await media.Parse(MediaParseOptions.ParseNetwork);
                    duration = media.Duration / 1000.0;
                }

                byte[] thumbnailBytes = await Task.Run(() =>
                {
                    try
                    {
                        using (var capture = new VideoCapture(video.FullPath))
                        {
                            sourceWidth = (int)capture.Get(Emgu.CV.CvEnum.CapProp.FrameWidth);
                            sourceHeight = (int)capture.Get(Emgu.CV.CvEnum.CapProp.FrameHeight);

                            int frameCount = (int)capture.Get(Emgu.CV.CvEnum.CapProp.FrameCount);
                            if (frameCount > 0)
                            {
                                capture.Set(Emgu.CV.CvEnum.CapProp.PosFrames, frameCount / 2);
                                using (var frame = new Mat())
                                {
                                    if (capture.Read(frame))
                                    {
                                        using (var bmp = frame.ToBitmap())
                                        using (var memory = new MemoryStream())
                                        {
                                            bmp.Save(memory, ImageFormat.Png);
                                            return memory.ToArray();
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"썸네일 생성 중 오류: {ex.Message}");
                    }
                    return Array.Empty<byte>();
                });

                if (thumbnailBytes.Length > 0)
                {
                    using (var memory = new MemoryStream(thumbnailBytes))
                    {
                        thumbnail = new BitmapImage();
                        thumbnail.BeginInit();
                        thumbnail.StreamSource = memory;
                        thumbnail.CacheOption = BitmapCacheOption.OnLoad;
                        thumbnail.EndInit();
                        thumbnail.Freeze();
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"비디오 정보 로드 중 오류 발생: {ex.Message}");
                duration = 10;
                thumbnail = null;
            }

            Debug.WriteLine($"    -> CreateVideoClipAsync: Name='{video.Title}', Position={position:F2}, Track={track}, Duration={duration:F2}");
            var newClip = new VideoClip
            {
                Name = video.Title,
                VideoPath = video.FullPath,
                Duration = duration,
                StartPosition = position,
                Width = duration * PixelsPerSecond,
                Thumbnail = thumbnail,
                Category = video.Category,
                TrackIndex = track,

                SourceWidth = sourceWidth,
                SourceHeight = sourceHeight
            };

            _ = GenerateWaveformForClipAsync(newClip, video.FullPath);

            newClip.UpdateWidth(this.PixelsPerSecond);
            return newClip;
        }

        private async Task<AudioClip?> CreateAudioClipAsync(Myvideo audio, double position, int track)
        {
            double duration = 0;
            try
            {
                using (var media = new Media(_libVLC, new Uri(audio.FullPath)))
                {
                    await media.Parse(MediaParseOptions.ParseNetwork);
                    duration = media.Duration / 1000.0;
                }

                if (duration <= 0) return null;

                Debug.WriteLine($"    -> CreateAudioClipAsync: Name='{audio.Title}', Position={position:F2}, Track={track}, Duration={duration:F2}");
                var newClip = new AudioClip
                {
                    Name = audio.Title,
                    AudioPath = audio.FullPath,
                    StartPosition = position,
                    TrackIndex = track,
                    Duration = duration,
                    Width = duration * PixelsPerSecond
                };

                _ = GenerateWaveformForClipAsync(newClip, audio.FullPath);

                return newClip;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error creating audio clip: {ex.Message}");
                return null;
            }
        }

        private Task<ImageClip?> CreateImageClipAsync(Myvideo image, double position, int track)
        {
            try
            {
                //  이미지 파일을 로드하여 BitmapImage 객체 생성
                var thumbnail = new BitmapImage();
                thumbnail.BeginInit();
                thumbnail.UriSource = new Uri(image.FullPath);
                thumbnail.CacheOption = BitmapCacheOption.OnLoad;
                thumbnail.EndInit(); // 이미지 로드가 완료 및 정보확인 가능
                thumbnail.Freeze(); // 다른 스레드에서 접근할 수 있도록 프리즈

                const double defaultDuration = 5.0; // 이미지 클립의 기본 길이

                var clip = new ImageClip
                {
                    Name = image.Title,
                    ImagePath = image.FullPath,
                    Thumbnail = thumbnail,
                    StartPosition = position,
                    TrackIndex = track,
                    Duration = defaultDuration,

                    // 로드된 BitmapImage에서 직접 픽셀 너비와 높이를 읽어와 저장.
                    SourceWidth = thumbnail.PixelWidth,
                    SourceHeight = thumbnail.PixelHeight,
                };

                clip.UpdateWidth(this.PixelsPerSecond); // 타임라인에서의 클립 너비 계산
                return Task.FromResult<ImageClip?>(clip);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"이미지 클립 생성 중 오류: {ex.Message}");
                return Task.FromResult<ImageClip?>(null);
            }
        }

        private void ExecuteClipMouseDown(MouseButtonEventArgs? e)
        {
            ClipInteractionStarted?.Invoke();
            if (e == null || (e.OriginalSource as FrameworkElement)?.Tag is "ResizeHandle") return;

            if ((e.Source as FrameworkElement)?.DataContext is TimelineClipBase clickedClip)
            {
                bool isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

                // 1. 그룹화된 클립을 클릭했는지 먼저 확인
                if (clickedClip.GroupId.HasValue)
                {
                    // 그룹 멤버를 클릭하면 항상 그룹 전체를 선택/선택 해제
                    var groupId = clickedClip.GroupId.Value;
                    var groupMembers = TimelineClips.Where(c => c.GroupId == groupId).ToList();
                    bool isGroupAlreadySelected = groupMembers.All(m => _selectedClips.Contains(m));

                    // 다른 모든 클립 선택 해제
                    foreach (var clip in _selectedClips.ToList())
                    {
                        clip.IsSelected = false;
                        _selectedClips.Remove(clip);
                    }

                    // 그룹이 이미 전체 선택된 상태가 아니라면, 그룹 전체를 선택
                    if (!isGroupAlreadySelected)
                    {
                        foreach (var member in groupMembers)
                        {
                            member.IsSelected = true;
                            _selectedClips.Add(member);
                        }
                    }
                }
                // 2. 그룹이 아닌 클립을 클릭한 경우 (기존 로직)
                else
                {
                    if (!isCtrlPressed)
                    {
                        if (!_selectedClips.Contains(clickedClip) || _selectedClips.Count > 1)
                        {
                            foreach (var clip in _selectedClips) clip.IsSelected = false;
                            _selectedClips.Clear();
                            clickedClip.IsSelected = true;
                            _selectedClips.Add(clickedClip);
                        }
                    }
                    else // Ctrl 키 누르고 클릭
                    {
                        if (clickedClip.IsSelected)
                        {
                            clickedClip.IsSelected = false;
                            _selectedClips.Remove(clickedClip);
                        }
                        else
                        {
                            clickedClip.IsSelected = true;
                            _selectedClips.Add(clickedClip);
                        }
                    }
                }

                // 마지막으로 선택/클릭된 클립을 Primary로 설정
                SelectedClip = _selectedClips.LastOrDefault();

                // [추가] 새 커맨드의 CanExecute 상태 업데이트
                GroupSelectedClipsCommand.NotifyCanExecuteChanged();
                UngroupSelectedClipsCommand.NotifyCanExecuteChanged();

                // 드래그 시작 준비
                if (_selectedClips.Any() && _selectedClips.Contains(clickedClip))
                {
                    IsDraggingClip = true;
                    var itemsControl = FindAncestor<System.Windows.Controls.ItemsControl>(e.Source as DependencyObject);
                    if (itemsControl != null) DragStartPoint = e.GetPosition(itemsControl);

                    DraggedClipsOriginalState.Clear();
                    foreach (var clip in _selectedClips)
                    {
                        DraggedClipsOriginalState[clip] = (clip.StartPosition, clip.TrackIndex);
                    }
                }
            }
        }

        public event Action? OnTimelineChanged;

        private void ExecuteClipMouseMove(MouseEventArgs? e)
        {
            if (!IsDraggingClip || _isResizing || e?.LeftButton != MouseButtonState.Pressed)
            {
                if (IsDraggingClip)
                {
                    IsDraggingClip = false;
                    DraggedClipsOriginalState.Clear();
                }
                return;
            }

            DataObject dragData = new DataObject("TimelineClips", _selectedClips.ToList());
            DragDropEffects result = DragDrop.DoDragDrop((DependencyObject)e.Source, dragData, DragDropEffects.Move);

            // If the drop was cancelled or invalid, revert the positions of the clips.
            if (result == DragDropEffects.None)
            {
                foreach (var clip in _selectedClips)
                {
                    if (DraggedClipsOriginalState.TryGetValue(clip, out var originalState))
                    {
                        clip.StartPosition = originalState.OriginalStart;
                        clip.TrackIndex = originalState.OriginalTrack;
                    }
                }
            }

            IsDraggingClip = false;
            DraggedClipsOriginalState.Clear();
            ClipInteractionEnded?.Invoke();
            OnTimelineChanged?.Invoke();
        }

        public void StartClipResize(TimelineClipBase clip, Point startPoint)
        {
            if (!(clip is ImageClip || clip is TextClip)) return;

            _isResizing = true;
            _resizingClip = clip;
            _resizeStartPoint = startPoint;
            _originalClipDuration = clip.Duration;

            if (SelectedClip != null && SelectedClip != clip)
            {
                SelectedClip.IsSelected = false;
            }
            clip.IsSelected = true;
            SelectedClip = clip;
        }

        public void UpdateClipResize(Point currentPoint)
        {
            if (!_isResizing || _resizingClip == null) return;

            double deltaX = currentPoint.X - _resizeStartPoint.X;
            double deltaTime = deltaX / PixelsPerSecond;
            double newDuration = Math.Max(0.1, _originalClipDuration + deltaTime);

            var nextClip = TimelineClips
                .Where(c => c.TrackIndex == _resizingClip.TrackIndex && c.Id != _resizingClip.Id && c.StartPosition > _resizingClip.StartPosition)
                .OrderBy(c => c.StartPosition)
                .FirstOrDefault();

            // 만약 바로 뒤에 다른 클립이 있다면, 길이를 조절하더라도 그 클립의 시작점을 침범할 수 없습니다.
            if (nextClip != null)
            {
                // 최대로 늘어날 수 있는 길이를 계산합니다.
                double maxDuration = nextClip.StartPosition - _resizingClip.StartPosition;
                // 계산된 새 길이가 최대 길이를 초과하지 않도록 제한합니다.
                newDuration = Math.Min(newDuration, maxDuration);
            }

            _resizingClip.Duration = newDuration;
            _resizingClip.UpdateWidth(PixelsPerSecond);
        }

        public void EndClipResize()
        {
            _isResizing = false;
            _resizingClip = null;
        }

        public double FindAdjustedPosition(TimelineClipBase movingClip, double desiredStartPosition, int desiredTrackIndex)
        {
            // 이동하려는 클립 자체를 제외하고, 같은 트랙에 있는 다른 모든 클립을 가져옵니다.
            var otherClipsInTrack = TimelineClips
                .Where(c => c.TrackIndex == desiredTrackIndex && c.Id != movingClip.Id)
                .OrderBy(c => c.StartPosition)
                .ToList();

            // 다른 클립이 없으면 원하는 위치에 그대로 두면 됩니다. (0보다 작은 값은 방지)
            if (!otherClipsInTrack.Any())
            {
                return Math.Max(0, desiredStartPosition);
            }

            double adjustedPosition = Math.Max(0, desiredStartPosition);
            bool collisionDetected;

            // 충돌이 더 이상 감지되지 않을 때까지 반복적으로 위치를 조정합니다.
            do
            {
                collisionDetected = false;
                double movingClipEndTime = adjustedPosition + movingClip.Duration;

                // 조정된 위치를 기준으로 충돌하는 클립이 있는지 확인합니다.
                var collidingClip = otherClipsInTrack.FirstOrDefault(other =>
                    adjustedPosition < (other.StartPosition + other.Duration) && // 내 시작이 다른 클립의 끝보다 빠르고,
                    movingClipEndTime > other.StartPosition                      // 내 끝이 다른 클립의 시작보다 늦으면 충돌!
                );

                if (collidingClip != null)
                {
                    // 충돌이 감지되면, 충돌한 클립의 바로 뒤로 내 위치를 옮깁니다(스냅).
                    adjustedPosition = collidingClip.StartPosition + collidingClip.Duration;
                    collisionDetected = true; // 위치가 변경되었으므로, 그 위치에서 또 다른 충돌이 있는지 다시 검사해야 합니다.
                }
            } while (collisionDetected);

            return adjustedPosition;
        }

        private void OnClipPropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TimelineClipBase.Duration))
            {
                if (sender is TimelineClipBase clip)
                {
                    clip.UpdateWidth(PixelsPerSecond);
                }
            }
        }

        public void Dispose()
        {
            _libVLC?.Dispose();
        }

        private static T? FindAncestor<T>(DependencyObject? current) where T : DependencyObject
        {
            if (current == null) return null;
            do
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);
            return null;
        }

    }
}
