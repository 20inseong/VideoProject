﻿using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using Emgu.CV;
using LibVLCSharp.Shared;
using VideoEditor.Common;
using VideoEditor.Models;
using Wpf.Ui.Input;

namespace VideoEditor.ViewModels
{
    public class ClipAddedEventArgs : EventArgs
    {
        public string VideoPath { get; }
        public TimelineClipBase Clip { get; }
        
        public ClipAddedEventArgs(string videoPath)
        {
            VideoPath = videoPath;
        }
        
        public ClipAddedEventArgs(TimelineClipBase clip, string videoPath = "")
        {
            Clip = clip;
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
        public TimelineClipBase? DraggedClip { get; private set; }
        private TimelineClipBase? _selectedClip;
        private readonly List<TimelineClipBase> _selectedClips = new List<TimelineClipBase>();
        private TimelineClipBase? _copiedClip;

        public Dictionary<TimelineClipBase, (double OriginalStart, int OriginalTrack)> DraggedClipsOriginalState { get; } = new Dictionary<TimelineClipBase, (double, int)>();

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



        public bool IsResizing => _isResizing;

        public ICommand DropOnTimelineCommand { get; }
        public ICommand ClipMouseDownCommand { get; }
        public ICommand ClipMouseMoveCommand { get; }
        public ICommand CopySelectedClipCommand { get; }
         public IRelayCommand<double> PasteClipCommand { get; }
        public ICommand ZoomInCommand { get; }
        public ICommand ZoomOutCommand { get; }
        public ICommand AddTextClipCommand { get; }
        public ICommand CreateSubtitlesFromTranscriptionCommand { get; }

        public ICommand SplitClipCommand { get; }

        public ICommand MoveClipsByKeyCommand { get; }

        public IRelayCommand<string> RotateSelectedClipCommand { get; }
        public ICommand ToggleMuteSelectedClipCommand { get; }

        public RelayCommand<object> GroupSelectedClipsCommand { get; }
        public RelayCommand<object> UngroupSelectedClipsCommand { get; }
        public RelayCommand<object> SeparateAudioCommand { get; }
        public RelayCommand<object> DeleteSelectedClipCommand { get; }

        public ObservableCollection<TimelineClipBase> TimelineClips
        {
            get => _timelineClips;
            set => SetProperty(ref _timelineClips, value);
        }

        public TimelineClipBase? SelectedClip
        {
            get => _selectedClip;
            set
            {
                if (SetProperty(ref _selectedClip, value))
                {
                    DeleteSelectedClipCommand.NotifyCanExecuteChanged();
                    (CopySelectedClipCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();

                    (CreateSubtitlesFromTranscriptionCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();

                    (_mainViewModel.AnalyzeEmotionCommand as IRelayCommand)?.NotifyCanExecuteChanged();

                    // ✨ [수정] 클립 선택이 변경될 때 감정 분석 커맨드의 상태를 갱신하도록 명시적으로 호출합니다.
                    _mainViewModel.AnalyzeEmotionCommand.NotifyCanExecuteChanged();
                }
            }
        }

        public double PixelsPerSecond
        {
            get => _pixelsPerSecond;
            set
            {
                double clampedValue = Math.Clamp(value, 0.02, 200.0);
                if (SetProperty(ref _pixelsPerSecond, clampedValue))
                {
                    foreach (var clip in TimelineClips)
                    {
                        clip.UpdateWidth(_pixelsPerSecond);
                        clip.OnPropertyChanged(nameof(clip.StartPosition));
                    }
                }
            }
        }

        private VideoClip? _currentlyPlayingClip;
        public VideoClip? CurrentlyPlayingClip
        {
            get => _currentlyPlayingClip;
            set => SetProperty(ref _currentlyPlayingClip, value);
        }

        private readonly MainViewModel _mainViewModel;

        public VideoEditorViewModel(MainViewModel mainViewModel)
        {
            _mainViewModel = mainViewModel;
            TimelineClips = new ObservableCollection<TimelineClipBase>();
            TimelineClips.CollectionChanged += (s, e) =>
            {
                if (e.NewItems != null)
                {
                    foreach (TimelineClipBase newClip in e.NewItems)
                    {
                        newClip.PropertyChanged += OnClipPropertyChanged;
                        newClip.UpdateWidth(PixelsPerSecond);
                    }
                }
                if (e.OldItems != null)
                {
                    foreach (TimelineClipBase oldClip in e.OldItems)
                    {
                        oldClip.PropertyChanged -= OnClipPropertyChanged;
                    }
                }
            };
            Core.Initialize();
            _libVLC = new LibVLC();
            _waveformService = new WaveformService();

            DropOnTimelineCommand = new RelayCommand<DragEventArgs>(ExecuteDropOnTimeline);
            ClipMouseDownCommand = new RelayCommand<MouseButtonEventArgs>(ExecuteClipMouseDown);
            ClipMouseMoveCommand = new RelayCommand<MouseEventArgs>(ExecuteClipMouseMove);
            DeleteSelectedClipCommand = new RelayCommand<object>(ExecuteDeleteSelectedClip, CanExecuteDeleteSelectedClip);

            CopySelectedClipCommand = new RelayCommand<object>(ExecuteCopySelectedClip, CanExecuteCopySelectedClip);
            PasteClipCommand = new RelayCommand<double>(ExecutePasteClip, CanExecutePasteClip);

            SplitClipCommand = new RelayCommand<double>(ExecuteSplitClip);

            ZoomInCommand = new RelayCommand<object>(_ => ZoomIn());
            ZoomOutCommand = new RelayCommand<object>(_ => ZoomOut());

            AddTextClipCommand = new RelayCommand<double>(ExecuteAddTextClip);

            CreateSubtitlesFromTranscriptionCommand = new RelayCommand<object>(ExecuteCreateSubtitlesFromTranscription, CanExecuteCreateSubtitlesFromTranscription);

            GroupSelectedClipsCommand = new RelayCommand<object>(ExecuteGroupSelectedClips, CanExecuteGroupSelectedClips);
            UngroupSelectedClipsCommand = new RelayCommand<object>(ExecuteUngroupSelectedClips, CanExecuteUngroupSelectedClips);
            SeparateAudioCommand = new RelayCommand<object>(ExecuteSeparateAudio, CanExecuteSeparateAudio);

            MoveClipsByKeyCommand = new RelayCommand<Key>(ExecuteMoveClipsByKey);

            RotateSelectedClipCommand = new RelayCommand<string>(ExecuteRotateSelectedClip, CanExecuteRotateSelectedClip);
            ToggleMuteSelectedClipCommand = new RelayCommand<object>(ExecuteToggleMuteSelectedClip, CanExecuteToggleMuteSelectedClip);
        }

        private bool CanExecuteRotateSelectedClip(string? degrees)
        {
            // 이미지 클립이 선택되었을 때만 회전 가능
            return SelectedClip is ImageClip;
        }

        private void ExecuteRotateSelectedClip(string? degrees)
        {
            if (SelectedClip is ImageClip imageClip && double.TryParse(degrees, out double angle))
            {
                // 현재 각도에 새로운 각도를 더하고 0-360도 사이로 정규화
                imageClip.Rotation = (imageClip.Rotation + angle + 360) % 360;
            }
        }

        private bool CanExecuteToggleMuteSelectedClip(object? _)
        {
            // 비디오 또는 오디오 클립일 때만 음소거 가능
            return SelectedClip is VideoClip || SelectedClip is AudioClip;
        }

        private void ExecuteToggleMuteSelectedClip(object? _)
        {
            if (SelectedClip is VideoClip vc)
            {
                vc.IsMuted = !vc.IsMuted;
            }
            else if (SelectedClip is AudioClip ac)
            {
                ac.IsMuted = !ac.IsMuted;
            }
        }

        private void ExecuteMoveClipsByKey(Key key)
        {
            if (!_selectedClips.Any()) return;

            double timeDelta = 0;
            if (key == Key.Left) timeDelta = -1.0;
            else if (key == Key.Right) timeDelta = 1.0;
            else return;

            // --- 충돌 감지 로직 시작 ---
            bool canMoveAllClips = true;
            foreach (var clip in _selectedClips)
            {
                // 선택된 각 클립이 새로운 위치로 이동 가능한지 검사
                if (!CanMoveTo(clip, clip.StartPosition + timeDelta, clip.TrackIndex, _selectedClips))
                {
                    canMoveAllClips = false; // 하나라도 이동 불가능하면 전체 이동을 막음
                    break;
                }
            }
            // --- 충돌 감지 로직 끝 ---

            // 모든 클립이 이동 가능할 때만 실제 위치를 변경합니다.
            if (canMoveAllClips)
            {
                foreach (var clip in _selectedClips)
                {
                    clip.StartPosition += timeDelta;
                }
                OnPropertyChanged(nameof(SelectedClip));
            }
        }
        private void ExecuteSeparateAudio(object? _)
        {
            if (!CanExecuteSeparateAudio(null)) return;

            var originalVideoClip = (VideoClip)SelectedClip;

            int audioTrackIndex = originalVideoClip.TrackIndex + 1;
            if (audioTrackIndex > 8)
            {
                _mainViewModel.HidePreviewObjectsForModal();
                _mainViewModel.HidePreviewObjectsForModal();
                MessageBox.Show("클립 바로 아래에 오디오를 추가할 트랙이 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                _mainViewModel.RestorePreviewObjectsAfterModal();
                _mainViewModel.RestorePreviewObjectsAfterModal();
                return;
            }

            bool isTrackOccupied = TimelineClips.Any(c =>
               c.TrackIndex == audioTrackIndex &&
               originalVideoClip.StartPosition < (c.StartPosition + c.Duration) &&
               (originalVideoClip.StartPosition + originalVideoClip.Duration) > c.StartPosition
           );

            if (isTrackOccupied)
            {
                _mainViewModel.HidePreviewObjectsForModal();
                _mainViewModel.HidePreviewObjectsForModal();
                MessageBox.Show("클립 바로 아래 트랙의 해당 시간대에 다른 클립이 있어 오디오를 분리할 수 없습니다.", "오류", MessageBoxButton.OK, MessageBoxImage.Warning);
                _mainViewModel.RestorePreviewObjectsAfterModal();
                _mainViewModel.RestorePreviewObjectsAfterModal();
                return;
            }

            var videoOnlyClip = (VideoClip)originalVideoClip.Clone();
            videoOnlyClip.IsMuted = true;
            videoOnlyClip.Name = $"{originalVideoClip.Name} (영상)";

            var audioOnlyClip = new AudioClip
            {
                Name = $"{originalVideoClip.Name} (오디오)",
                AudioPath = originalVideoClip.VideoPath,
                StartPosition = originalVideoClip.StartPosition,
                SourceStartTime = originalVideoClip.SourceStartTime,
                TrackIndex = audioTrackIndex,
                Volume = originalVideoClip.Volume,
                SpeedRatio = originalVideoClip.SpeedRatio,
                Duration = originalVideoClip.Duration,
            };
            audioOnlyClip.UpdateWidth(this.PixelsPerSecond);

            var newGroupId = Guid.NewGuid();
            videoOnlyClip.GroupId = newGroupId;
            audioOnlyClip.GroupId = newGroupId;

            TimelineClips.Remove(originalVideoClip);
            _selectedClips.Remove(originalVideoClip);

            TimelineClips.Add(videoOnlyClip);
            TimelineClips.Add(audioOnlyClip);

            // Generate waveform for the separated audio clip
            _ = GenerateWaveformForClipAsync(audioOnlyClip, audioOnlyClip.AudioPath);

            videoOnlyClip.IsSelected = true;
            audioOnlyClip.IsSelected = true;
            _selectedClips.Add(videoOnlyClip);
            _selectedClips.Add(audioOnlyClip);
            SelectedClip = videoOnlyClip;

            // 분리 후에는 오디오 분리 커맨드를 비활성화
            SeparateAudioCommand.NotifyCanExecuteChanged();
        }

        private bool CanExecuteSeparateAudio(object? _)
        {
            return _selectedClips.Count == 1 && SelectedClip is VideoClip vc && !vc.IsMuted;
        }

        private void ExecuteGroupSelectedClips(object? _)
        {
            if (!CanExecuteGroupSelectedClips(null)) return;
            var newGroupId = Guid.NewGuid();
            foreach (var clip in _selectedClips)
            {
                clip.GroupId = newGroupId;
            }
            Debug.WriteLine($"[Group] {_selectedClips.Count}개의 클립이 그룹(ID: {newGroupId})으로 묶였습니다.");
            UngroupSelectedClipsCommand.NotifyCanExecuteChanged();
        }

        private bool CanExecuteGroupSelectedClips(object? _)
        {
            return _selectedClips.Count >= 2;
        }

        private void ExecuteUngroupSelectedClips(object? _)
        {
            if (!CanExecuteUngroupSelectedClips(null)) return;

            var groupIdsToUngroup = _selectedClips
                .Where(c => c.GroupId.HasValue)
                .Select(c => c.GroupId.Value)
                .Distinct()
                .ToList();

            var clipsToUngroup = TimelineClips
                .Where(c => c.GroupId.HasValue && groupIdsToUngroup.Contains(c.GroupId.Value))
                .ToList();

            foreach (var clip in clipsToUngroup)
            {
                clip.GroupId = null;
            }
            Debug.WriteLine($"[Group] {clipsToUngroup.Count}개의 클립이 포함된 {groupIdsToUngroup.Count}개의 그룹이 해제되었습니다.");
            UngroupSelectedClipsCommand.NotifyCanExecuteChanged();
        }

        private bool CanExecuteUngroupSelectedClips(object? _)
        {
            return _selectedClips.Any(c => c.GroupId.HasValue);
        }


        private bool CanExecuteCreateSubtitlesFromTranscription(object? _)
        {
            if (SelectedClip == null) return false;

            if (SelectedClip is VideoClip vc && vc.Transcription.Any()) return true;
            if (SelectedClip is AudioClip ac && ac.Transcription.Any()) return true;

            return false;
        }

        private void ExecuteCreateSubtitlesFromTranscription(object? _)
        {
            // CanExecuteCreateSubtitlesFromTranscription 메서드 또한 object? 매개변수를 받도록 수정해야 합니다.
            // CanExecute..() -> CanExecute..(null)
            if (!CanExecuteCreateSubtitlesFromTranscription(null)) return;

            var transcriptionOwnerClip = SelectedClip;
            ObservableCollection<TranscriptionSegment>? segments = null;

            if (transcriptionOwnerClip is VideoClip vc) segments = vc.Transcription;
            else if (transcriptionOwnerClip is AudioClip ac) segments = ac.Transcription;

            if (segments == null) return;

            _mainViewModel.HidePreviewObjectsForModal();
            // Ensure playback is paused before popup
            if (_mainViewModel.IsTimelinePlaying)
            {
                _mainViewModel.StopPlayback();
            }
            _mainViewModel.HidePreviewObjectsForModal();
            var result = MessageBox.Show(
                $"{segments.Count}개의 자막 클립을 타임라인에 추가하시겠습니까?",
                "자막 생성 확인",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);
            _mainViewModel.RestorePreviewObjectsAfterModal();

            if (result == MessageBoxResult.No) return;

            foreach (var segment in segments)
            {
                double newTextClipStart = transcriptionOwnerClip.StartPosition + segment.Start.TotalSeconds;
                double newTextClipDuration = segment.Duration.TotalSeconds;

                if (newTextClipDuration < 0.1) newTextClipDuration = 0.1;

                int trackIndex = FindAvailableTrack(newTextClipStart, newTextClipDuration, 4, -1);

                var newTextClip = new TextClip
                {
                    Name = "자막",
                    Text = segment.Text.Trim(),
                    StartPosition = newTextClipStart,
                    Duration = newTextClipDuration,
                    TrackIndex = trackIndex
                    // X, Y, RenderWidth, RenderHeight는 MainViewModel의 InitializeTextClipLayout에서 설정됨
                };

                TimelineClips.Add(newTextClip);
            }
            Debug.WriteLine($"[Subtitle Gen] {segments.Count}개의 자막 클립이 생성되었습니다.");
        }

        private void ExecuteAddTextClip(double creationTime)
        {
            const double defaultDuration = 5.0; // 자막 기본 길이 5초

            var newClip = new TextClip
            {
                Name = "새 자막",
                Text = "자막을 입력하세요",
                StartPosition = creationTime,
                Duration = defaultDuration,
                Width = defaultDuration * PixelsPerSecond,
                TrackIndex = FindAvailableTrack(creationTime, defaultDuration)
                // X, Y, RenderWidth, RenderHeight는 MainViewModel의 InitializeTextClipLayout에서 설정됨
            };
            TimelineClips.Add(newClip);
        }

        private void ZoomIn()
        {
            PixelsPerSecond *= 1.6;
        }

        private void ZoomOut()
        {
            PixelsPerSecond /= 1.6;
        }

        private void ExecuteCopySelectedClip(object? _)
        {
            if (SelectedClip == null) return;

            _copiedClip = SelectedClip.Clone();
            PasteClipCommand.NotifyCanExecuteChanged();
            Console.WriteLine($"[Copy LOG] '{_copiedClip.Name}' 클립이 복사되었습니다.");
        }

        private bool CanExecuteCopySelectedClip(object? _)
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
            return FindAvailableTrack(startTime, duration, 0, 1);
        }

        private int FindAvailableTrack(double startTime, double duration, int startTrack, int direction)
        {
            if (direction > 0) // 위에서 아래로 (0 -> 8)
            {
                for (int track = startTrack; track <= 8; track++)
                {
                    bool isOccupied = TimelineClips.Any(c =>
                        c.TrackIndex == track &&
                        startTime < (c.StartPosition + c.Duration) &&
                        (startTime + duration) > c.StartPosition);
                    if (!isOccupied) return track;
                }
            }
            else // 아래에서 위로 (8 -> 0)
            {
                for (int track = startTrack; track >= 0; track--)
                {
                    bool isOccupied = TimelineClips.Any(c =>
                       c.TrackIndex == track &&
                       startTime < (c.StartPosition + c.Duration) &&
                       (startTime + duration) > c.StartPosition);
                    if (!isOccupied) return track;
                }
            }
            // 모든 트랙이 꽉 찼으면 원래 시작하려던 트랙을 반환
            return Math.Clamp(startTrack, 0, 8);
        }

        private void ExecuteDeleteSelectedClip(object? _)
        {
            if (SelectedClip != null)
            {
                TimelineClips.Remove(SelectedClip);
                SelectedClip = null;
                //Console.WriteLine("[Delete LOG] 클립이 삭제되었습니다.");
            }
        }

        private bool CanExecuteDeleteSelectedClip(object? _)
        {
            return SelectedClip != null;
        }

        private void ExecuteSplitClip(double currentTimelinePosition)
        {
            var originalClip = TimelineClips.FirstOrDefault(c =>
                c.StartPosition < currentTimelinePosition && (c.StartPosition + c.Duration) > currentTimelinePosition);

            if (originalClip == null) return;

            double originalDuration = originalClip.Duration;
            double splitPointInClip = currentTimelinePosition - originalClip.StartPosition;
            double newClipDuration = originalDuration - splitPointInClip;

            // 원본 클립의 지속시간을 분할 지점까지로 설정
            originalClip.Duration = splitPointInClip;
            originalClip.UpdateWidth(this.PixelsPerSecond);

            TimelineClipBase? newClip = null;

            switch (originalClip)
            {
                case VideoClip vc:
                    // SourceStartTime 계산 시 배속을 고려
                    // 타임라인에서 splitPointInClip 만큼 지났으므로, 실제 소스에서는 splitPointInClip * SpeedRatio 만큼 지남
                    double sourceTimeDelta = splitPointInClip * vc.SpeedRatio;
                    
                    var newVideoClip = new VideoClip
                    {
                        Name = vc.Name + " (2)",
                        VideoPath = vc.VideoPath,
                        Thumbnail = vc.Thumbnail,
                        Category = vc.Category,
                        StartPosition = currentTimelinePosition,
                        TrackIndex = vc.TrackIndex,
                        SourceStartTime = vc.SourceStartTime + sourceTimeDelta,
                        SourceWidth = vc.SourceWidth,
                        SourceHeight = vc.SourceHeight,
                        X = vc.X,
                        Y = vc.Y,
                        RenderWidth = vc.RenderWidth,
                        RenderHeight = vc.RenderHeight,
                        Volume = vc.Volume,
                        SpeedRatio = vc.SpeedRatio,
                        IsMuted = vc.IsMuted,
                        Scale = vc.Scale,
                        Duration = newClipDuration
                    };
                    
                    // 파형 데이터 복사
                    newVideoClip.WaveformData = new System.Collections.Generic.List<System.Windows.Point>(vc.WaveformData);
                    
                    // 자막 데이터 복사 (시간 조정 필요)
                    foreach (var segment in vc.Transcription)
                    {
                        // 분할 지점 이후의 자막만 새 클립에 추가
                        if (segment.Start.TotalSeconds >= splitPointInClip)
                        {
                            var newSegment = new TranscriptionSegment
                            {
                                Start = TimeSpan.FromSeconds(segment.Start.TotalSeconds - splitPointInClip),
                                End = TimeSpan.FromSeconds(segment.End.TotalSeconds - splitPointInClip),
                                Text = segment.Text
                            };
                            newVideoClip.Transcription.Add(newSegment);
                        }
                    }
                    
                    // 원본 클립의 자막도 분할 지점까지만 유지
                    var segmentsToRemove = vc.Transcription.Where(s => s.Start.TotalSeconds >= splitPointInClip).ToList();
                    foreach (var segment in segmentsToRemove)
                    {
                        vc.Transcription.Remove(segment);
                    }
                    
                    newClip = newVideoClip;
                    break;

                case AudioClip ac:
                    // SourceStartTime 계산 시 배속을 고려
                    double audioSourceTimeDelta = splitPointInClip * ac.SpeedRatio;
                    
                    var newAudioClip = new AudioClip
                    {
                        Name = ac.Name + " (2)",
                        AudioPath = ac.AudioPath,
                        StartPosition = currentTimelinePosition,
                        TrackIndex = ac.TrackIndex,
                        SourceStartTime = ac.SourceStartTime + audioSourceTimeDelta,
                        Volume = ac.Volume,
                        SpeedRatio = ac.SpeedRatio,
                        IsMuted = ac.IsMuted,
                        Scale = ac.Scale,
                        Duration = newClipDuration
                    };
                    
                    // 파형 데이터 복사
                    newAudioClip.WaveformData = new System.Collections.Generic.List<System.Windows.Point>(ac.WaveformData);
                    
                    // 자막 데이터 복사 (시간 조정 필요)
                    foreach (var segment in ac.Transcription)
                    {
                        if (segment.Start.TotalSeconds >= splitPointInClip)
                        {
                            var newSegment = new TranscriptionSegment
                            {
                                Start = TimeSpan.FromSeconds(segment.Start.TotalSeconds - splitPointInClip),
                                End = TimeSpan.FromSeconds(segment.End.TotalSeconds - splitPointInClip),
                                Text = segment.Text
                            };
                            newAudioClip.Transcription.Add(newSegment);
                        }
                    }
                    
                    // 원본 클립의 자막도 분할 지점까지만 유지
                    var audioSegmentsToRemove = ac.Transcription.Where(s => s.Start.TotalSeconds >= splitPointInClip).ToList();
                    foreach (var segment in audioSegmentsToRemove)
                    {
                        ac.Transcription.Remove(segment);
                    }
                    
                    newClip = newAudioClip;
                    break;
                    
                case ImageClip ic:
                    var newImageClip = new ImageClip
                    {
                        Name = ic.Name + " (2)",
                        ImagePath = ic.ImagePath,
                        Thumbnail = ic.Thumbnail,
                        StartPosition = currentTimelinePosition,
                        TrackIndex = ic.TrackIndex,
                        SourceWidth = ic.SourceWidth,
                        SourceHeight = ic.SourceHeight,
                        X = ic.X,
                        Y = ic.Y,
                        RenderWidth = ic.RenderWidth,
                        RenderHeight = ic.RenderHeight,
                        Volume = ic.Volume,
                        SpeedRatio = ic.SpeedRatio,
                        IsMuted = ic.IsMuted,
                        Scale = ic.Scale,
                        Duration = newClipDuration,
                        Rotation = ic.Rotation,
                        Opacity = ic.Opacity,
                        CustomWidth = ic.CustomWidth,
                        CustomHeight = ic.CustomHeight,
                        InitialRenderWidth = ic.InitialRenderWidth,
                        InitialRenderHeight = ic.InitialRenderHeight
                    };
                    
                    newClip = newImageClip;
                    break;
                    
                case TextClip tc:
                    var newTextClip = new TextClip
                    {
                        Name = tc.Name + " (2)",
                        Text = tc.Text,
                        FontSize = tc.FontSize,
                        StartPosition = currentTimelinePosition,
                        TrackIndex = tc.TrackIndex,
                        X = tc.X,
                        Y = tc.Y,
                        RenderWidth = tc.RenderWidth,
                        RenderHeight = tc.RenderHeight,
                        Volume = tc.Volume,
                        SpeedRatio = tc.SpeedRatio,
                        IsMuted = tc.IsMuted,
                        Scale = tc.Scale,
                        Duration = newClipDuration
                    };
                    
                    newClip = newTextClip;
                    break;
            }

            if (newClip != null)
            {
                newClip.UpdateWidth(this.PixelsPerSecond);

                int originalClipIndex = TimelineClips.IndexOf(originalClip);
                if (originalClipIndex != -1)
                {
                    TimelineClips.Insert(originalClipIndex + 1, newClip);
                }
                else
                {
                    TimelineClips.Add(newClip);
                }

                Debug.WriteLine($"[Split LOG] '{originalClip.Name}' 클립 자르기 완료. 새 클립 '{newClip.Name}' 생성됨.");
            }
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

        private async void ExecuteDropOnTimeline(DragEventArgs? e)
        {
            if (e == null) return;

            if (e.Source is not FrameworkElement dropTarget)
            {
                return;
            }

            try
            {
                if (e.Data.GetDataPresent("TimelineClips") && e.Data.GetData("TimelineClips") is List<TimelineClipBase> droppedClips)
                {
                    Point finalDropPosition = e.GetPosition(dropTarget);
                    double deltaX = finalDropPosition.X - DragStartPoint.X;
                    double deltaTime = deltaX / this.PixelsPerSecond;
                    int deltaTrack = (int)Math.Round((finalDropPosition.Y - DragStartPoint.Y) / 60.0);

                    bool canMoveAllClips = true;
                    foreach (var clip in droppedClips)
                    {
                        if (DraggedClipsOriginalState.TryGetValue(clip, out var originalState))
                        {
                            double desiredStartPosition = originalState.OriginalStart + deltaTime;
                            int newTrackIndex = Math.Clamp(originalState.OriginalTrack + deltaTrack, 0, 8);

                            if (!CanMoveTo(clip, desiredStartPosition, newTrackIndex, droppedClips))
                            {
                                canMoveAllClips = false;
                                break;
                            }
                        }
                        else // 드래그 시작 상태를 찾을 수 없는 예외적인 경우
                        {
                            canMoveAllClips = false;
                            break;
                        }
                    }

                    if (canMoveAllClips)
                    {
                        // [이동 성공] 모든 클립이 이동 가능하므로 최종 위치를 확정합니다.
                        // (이 부분은 DragOver에서 이미 위치가 변경되었으므로 별도 코드가 필요 없습니다.)
                        // 최종적으로 플레이어만 동기화해줍니다.
                        if (droppedClips.Any(c => c is VideoClip))
                        {
                            _mainViewModel.TriggerVideoClipZOrderUpdate();
                        }
                        _mainViewModel.SyncPlayersToTimeline();
                    }
                    else
                    {
                        // ⭐ [이동 실패] 겹치는 위치이거나 타임라인 끝을 넘어간 경우
                        // 기준이 되는 클립 (그룹 이동 시 위치 계산의 기준점)
                        var primaryClip = droppedClips.OrderBy(c => c.StartPosition).First();
                        var primaryClipOriginalState = DraggedClipsOriginalState[primaryClip];
                        double primaryClipDesiredStart = primaryClipOriginalState.OriginalStart + deltaTime;
                        int primaryClipNewTrack = Math.Clamp(primaryClipOriginalState.OriginalTrack + deltaTrack, 0, 8);

                        // 타겟 트랙의 마지막 클립이 끝나는 시간을 찾습니다.
                        double endOfTrack = GetTimelineEndOfTrack(primaryClipNewTrack, droppedClips);

                        // 사용자가 타임라인의 기존 콘텐츠 끝 너머로 드래그했는지 확인합니다.
                        if (primaryClipDesiredStart >= endOfTrack)
                        {
                            // [스냅 성공] 사용자의 의도를 '끝에 붙이기'로 판단하고, 위치를 보정하여 스냅합니다.
                            // 기준 클립을 트랙 끝에 붙이기 위해 필요한 시간 보정치 계산
                            double timeOffset = endOfTrack - primaryClipDesiredStart;

                            foreach (var clip in droppedClips)
                            {
                                var originalState = DraggedClipsOriginalState[clip];
                                // 모든 클립에 동일한 보정치를 적용하여 그룹 형태를 유지하며 이동
                                clip.StartPosition = originalState.OriginalStart + deltaTime + timeOffset;
                                clip.TrackIndex = Math.Clamp(originalState.OriginalTrack + deltaTrack, 0, 8);
                            }

                            if (droppedClips.Any(c => c is VideoClip))
                            {
                                _mainViewModel.TriggerVideoClipZOrderUpdate();
                            }
                            _mainViewModel.SyncPlayersToTimeline();
                        }
                        else
                        {
                            // [단순 충돌] 타임라인 중간에서 다른 클립과 겹친 것이므로 원래 위치로 되돌립니다.
                            foreach (var clip in droppedClips)
                            {
                                if (DraggedClipsOriginalState.TryGetValue(clip, out var originalState))
                                {
                                    clip.StartPosition = originalState.OriginalStart;
                                    clip.TrackIndex = originalState.OriginalTrack;
                                }
                            }
                        }
                    }
                }
                else if (e.Data.GetDataPresent("Myvideo"))
                {
                    if (e.Data.GetData("Myvideo") is not Myvideo droppedVideo || !System.IO.File.Exists(droppedVideo.FullPath))
                    {
                        return;
                    }

                    Point dropPosition = e.GetPosition(dropTarget);
                    double startTimeInSeconds = dropPosition.X / this.PixelsPerSecond;
                    int trackIndex = (int)(dropPosition.Y / 60.0);
                    trackIndex = Math.Clamp(trackIndex, 0, 4);

                    await AddMediaClipAsync(droppedVideo, startTimeInSeconds, trackIndex);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"드롭 처리 중 오류 발생: {ex.Message}");
            }
            finally
            {
                // MainWindow의 스냅 라인을 정리하도록 요청 (직접 접근 대신 이벤트나 콜백이 더 좋지만, 지금은 간단한 방법 사용)
                (_mainViewModel.GetMainWindow() as MainWindow)?.ClearSnapIndicators();
                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    ClipInteractionEnded?.Invoke();
                }, System.Windows.Threading.DispatcherPriority.Background);
            }
        }
        public async Task AddMediaClipAsync(Myvideo media, double dropPosition, int trackIndex)
        {
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
                OnClipAdded?.Invoke(this, new ClipAddedEventArgs(newClip, media.FullPath));
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

                    if (duration <= 0 || media.Tracks == null || !media.Tracks.Any(t => t.TrackType == TrackType.Video))
                    {
                        Debug.WriteLine($"[Validation] 손상되었거나 비디오 트랙이 없는 파일은 건너뜁니다: {video.FullPath}");
                        _mainViewModel.HidePreviewObjectsForModal();
                MessageBox.Show($"'{System.IO.Path.GetFileName(video.FullPath)}' 파일이 손상되었거나 비디오 트랙이 없어 타임라인에 추가할 수 없습니다.",
                                        "비디오 추가 실패", MessageBoxButton.OK, MessageBoxImage.Warning);
                _mainViewModel.RestorePreviewObjectsAfterModal();
                        return null;
                    }
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
                Debug.WriteLine($"비디오 정보 로드 중 오류 발생 (손상 가능성 있음): {ex.Message}");
                _mainViewModel.HidePreviewObjectsForModal();
                MessageBox.Show($"'{System.IO.Path.GetFileName(video.FullPath)}' 파일을 처리하는 중 오류가 발생했습니다. 파일이 손상되었을 수 있습니다.",
                                "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                _mainViewModel.RestorePreviewObjectsAfterModal();
                return null;
            }

            double playerHostWidth = _mainViewModel.PlayerHostWidth;
            double playerHostHeight = _mainViewModel.PlayerHostHeight;
            
            // 컨트롤 바 높이를 고려하여 실제 비디오 표시 영역 계산
            const double controlBarHeight = 50; // 컨트롤 바의 대략적인 높이
            double availableVideoHeight = playerHostHeight - controlBarHeight;

            double renderWidth, renderHeight, x, y;

            if (sourceWidth > 0 && sourceHeight > 0 && playerHostWidth > 1 && availableVideoHeight > 1)
            {
                double playerAspectRatio = playerHostWidth / availableVideoHeight;
                double videoAspectRatio = (double)sourceWidth / sourceHeight;

                if (playerAspectRatio > videoAspectRatio)
                {
                    renderHeight = availableVideoHeight;
                    renderWidth = renderHeight * videoAspectRatio;
                }
                else
                {
                    renderWidth = playerHostWidth;
                    renderHeight = renderWidth / videoAspectRatio;
                }

                x = (playerHostWidth - renderWidth) / 2;
                y = (availableVideoHeight - renderHeight) / 2;
            }
            else
            {
                renderWidth = sourceWidth;
                renderHeight = sourceHeight;
                x = 0;
                y = 0;
            }

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
                SourceHeight = sourceHeight,
                
                X = x,
                Y = y,
                RenderWidth = renderWidth,
                RenderHeight = renderHeight
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
                
                int sourceWidth = thumbnail.PixelWidth;
                int sourceHeight = thumbnail.PixelHeight;
                
                double playerHostWidth = _mainViewModel.PlayerHostWidth;
                double playerHostHeight = _mainViewModel.PlayerHostHeight;
                
                // 컨트롤 바 높이를 고려하여 실제 비디오 표시 영역 계산
                const double controlBarHeight = 50;
                double availableVideoHeight = playerHostHeight - controlBarHeight;

                double renderWidth, renderHeight, x, y;

                if (sourceWidth > 0 && sourceHeight > 0 && playerHostWidth > 1 && availableVideoHeight > 1)
                {
                    double playerAspectRatio = playerHostWidth / availableVideoHeight;
                    double imageAspectRatio = (double)sourceWidth / sourceHeight;

                    if (playerAspectRatio > imageAspectRatio)
                    {
                        renderHeight = availableVideoHeight;
                        renderWidth = renderHeight * imageAspectRatio;
                    }
                    else
                    {
                        renderWidth = playerHostWidth;
                        renderHeight = renderWidth / imageAspectRatio;
                    }

                    x = (playerHostWidth - renderWidth) / 2;
                    y = (availableVideoHeight - renderHeight) / 2;
                }
                else
                {
                    renderWidth = sourceWidth;
                    renderHeight = sourceHeight;
                    x = 0;
                    y = 0;
                }

                var clip = new ImageClip
                {
                    Name = image.Title,
                    ImagePath = image.FullPath,
                    Thumbnail = thumbnail,
                    StartPosition = position,
                    TrackIndex = track,
                    Duration = defaultDuration,

                    // 로드된 BitmapImage에서 직접 픽셀 너비와 높이를 읽어와 저장.
                    SourceWidth = sourceWidth,
                    SourceHeight = sourceHeight,

                    X = x,
                    Y = y,
                    RenderWidth = renderWidth,
                    RenderHeight = renderHeight,
                    
                    // 초기 렌더 크기 저장 (비율 계산에 사용)
                    InitialRenderWidth = renderWidth,
                    InitialRenderHeight = renderHeight,
                    
                    // 초기 크기를 원본 크기로 설정
                    CustomWidth = sourceWidth,
                    CustomHeight = sourceHeight
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
            if (e == null || (e.OriginalSource as FrameworkElement)?.Tag is "ResizeHandle") return;

            if ((e.Source as FrameworkElement)?.DataContext is TimelineClipBase clickedClip)
            {
                if (clickedClip is VideoClip videoClip)
                {
                    Debug.WriteLine($"[Clip Clicked] Name: '{videoClip.Name}', IsEmotionAnalyzed: {videoClip.IsEmotionAnalyzed}");
                }
                else
                {
                    Debug.WriteLine($"[Clip Clicked] Name: '{clickedClip.Name}', Type: {clickedClip.GetType().Name} (Not a VideoClip)");
                }

                bool isCtrlPressed = Keyboard.IsKeyDown(Key.LeftCtrl) || Keyboard.IsKeyDown(Key.RightCtrl);

                // 1. 그룹화된 클립을 클릭했는지 확인
                if (clickedClip.GroupId.HasValue)
                {
                    var groupId = clickedClip.GroupId.Value;
                    var groupMembers = TimelineClips.Where(c => c.GroupId == groupId).ToList();

                    // Ctrl 키 없이 그룹 클릭 시 : 다른 모든 선택 해제 후 그룹 전체 선택
                    if (!isCtrlPressed)
                    {
                        foreach (var clip in TimelineClips) clip.IsSelected = false;
                        _selectedClips.Clear();

                        foreach (var member in groupMembers)
                        {
                            member.IsSelected = true;
                            _selectedClips.Add(member);
                        }
                    }
                    else // Ctrl 키 누르고 그룹 클릭 시 : 그룹 전체를 선택 목록에 추가/제거
                    {
                        bool isGroupFullySelected = groupMembers.All(m => _selectedClips.Contains(m));
                        foreach (var member in groupMembers)
                        {
                            if (isGroupFullySelected) // 이미 전체 선택됐다면 선택 해제
                            {
                                member.IsSelected = false;
                                _selectedClips.Remove(member);
                            }
                            else if (!_selectedClips.Contains(member)) // 선택 안된 멤버만 선택 추가
                            {
                                member.IsSelected = true;
                                _selectedClips.Add(member);
                            }
                        }
                    }
                }
                // 2. 그룹이 아닌 클립을 클릭한 경우
                else
                {
                    if (!isCtrlPressed)
                    {
                        // 클릭한 클립이 선택 목록에 없거나, 여러 개가 선택된 상태였다면
                        if (!_selectedClips.Contains(clickedClip) || _selectedClips.Count > 1)
                        {
                            foreach (var clip in TimelineClips) clip.IsSelected = false;
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

                // 주 선택 클립(속성창에 표시될 클립) 업데이트
                SelectedClip = _selectedClips.LastOrDefault();

                // 커맨드 상태 갱신
                GroupSelectedClipsCommand.NotifyCanExecuteChanged();
                UngroupSelectedClipsCommand.NotifyCanExecuteChanged();
                SeparateAudioCommand.NotifyCanExecuteChanged();

                if (_selectedClips.Any() && _selectedClips.Contains(clickedClip))
                {
                    // 드래그 후보만 표시하고 실제 드래그 시작은 MouseMove에서 임계치를 넘었을 때 처리
                    var itemsControl = (e.Source as FrameworkElement)?.FindAncestor<ItemsControl>();
                    if (itemsControl != null) DragStartPoint = e.GetPosition(itemsControl);

                    //// 선택된 모든 클립의 현재 상태를 저장 (여기서만 캡처)
                    //DraggedClipsOriginalState.Clear();
                    //foreach (var clip in _selectedClips)
                    //{
                    //    DraggedClipsOriginalState[clip] = (clip.StartPosition, clip.TrackIndex);
                    //}

                    // 드래그 시작 신호는 MouseMove에서 임계치 통과 시에만 보냅니다.
                }
            }
        }

        private void ExecuteClipMouseMove(MouseEventArgs? e)
        {
            if (_isResizing || e == null)
            {
                return;
            }

            // 왼쪽 버튼이 눌린 상태에서만 드래그 검사
            if (e.LeftButton != MouseButtonState.Pressed)
            {
                // 안전장치: 눌리지 않았다면 드래그 상태 해제
                if (IsDraggingClip)
                {
                    IsDraggingClip = false;
                    DraggedClipsOriginalState.Clear();
                }
                return;
            }

            // 임계치 기반 드래그 시작 판단
            var itemsControl = (e.Source as FrameworkElement)?.FindAncestor<System.Windows.Controls.ItemsControl>();
            System.Windows.Point currentPos = itemsControl != null ? e.GetPosition(itemsControl) : new System.Windows.Point();

            if (!IsDraggingClip)
            {
                double dx = Math.Abs(currentPos.X - DragStartPoint.X);
                double dy = Math.Abs(currentPos.Y - DragStartPoint.Y);
                if (dx < SystemParameters.MinimumHorizontalDragDistance && dy < SystemParameters.MinimumVerticalDragDistance)
                {
                    return; // 아직 드래그 아님 (단순 클릭)
                }
                IsDraggingClip = true; // 여기서 실제 드래그 시작
            }
            // 드래그가 끝난 뒤, 재생을 재개할지 여부는 MainViewModel.ResumePlaybackIfNeeded에서 처리됨


            // 드래그 시작: 재생 일시중지 신호
            ClipInteractionStarted?.Invoke();

            DraggedClipsOriginalState.Clear();
            foreach (var clip in _selectedClips)
            {
                DraggedClipsOriginalState[clip] = (clip.StartPosition, clip.TrackIndex);
            }

            // 선택된 모든 클립을 함께 드래그
            DataObject dragData = new DataObject("TimelineClips", _selectedClips.ToList());
            DragDropEffects result = DragDrop.DoDragDrop((DependencyObject)e.Source, dragData, DragDropEffects.Move);

            // 드롭이 취소된 경우 원복
            if (result == DragDropEffects.None)
            {
                (_mainViewModel.GetMainWindow() as MainWindow)?.ClearSnapIndicators();
                foreach (var clip in _selectedClips)
                {
                    if (DraggedClipsOriginalState.TryGetValue(clip, out var originalState))
                    {
                        clip.StartPosition = originalState.OriginalStart;
                        clip.TrackIndex = originalState.OriginalTrack;
                    }
                }
                ClipInteractionEnded?.Invoke();
            }

            // 드래그 종료: 상태 초기화
            IsDraggingClip = false;
            DraggedClipsOriginalState.Clear();

            if (_selectedClips.Any(c => c is VideoClip))
            {
                _mainViewModel.TriggerVideoClipZOrderUpdate();
            }
            // 실제 드롭 완료 시점에는 ExecuteDropOnTimeline에서 ClipInteractionEnded 호출됨
        }

        public void StartClipResize(TimelineClipBase clip, Point startPoint)
        {
            if (!(clip is ImageClip || clip is TextClip || clip is VideoClip)) return;

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

            // 최소 길이를 0.1초로 제한하여 클립이 사라지는 것을 방지
            double newDuration = Math.Max(0.1, _originalClipDuration + deltaTime);

            _resizingClip.Duration = newDuration;
            _resizingClip.UpdateWidth(PixelsPerSecond);
        }

        public void EndClipResize()
        {
            _isResizing = false;
            _resizingClip = null;
        }

        private double AdjustClipPosition(TimelineClipBase movingClip, double desiredStartPosition, int desiredTrackIndex)
        {
            double newStartPosition = Math.Max(0, desiredStartPosition);

            var otherClipsInTrack = TimelineClips
                .Where(c => c.TrackIndex == desiredTrackIndex && c.Id != movingClip.Id)
                .OrderBy(c => c.StartPosition)
                .ToList();

            foreach (var otherClip in otherClipsInTrack)
            {
                if (newStartPosition < (otherClip.StartPosition + otherClip.Duration) &&
                    (movingClip.StartPosition + movingClip.Duration) > otherClip.StartPosition) 
                {
                    newStartPosition = otherClip.StartPosition + otherClip.Duration;
                }
            }

            foreach (var otherClip in otherClipsInTrack)
            {
                if (otherClip.StartPosition < (newStartPosition + movingClip.Duration) &&
                    otherClip.StartPosition > newStartPosition)
                {
                    newStartPosition = otherClip.StartPosition - movingClip.Duration;
                }
            }

            return Math.Max(0, newStartPosition);
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

            if (sender == SelectedClip)
            {
                (CreateSubtitlesFromTranscriptionCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
            }
        }
        
        /// <summary>
        /// Updates internal selected clips list and synchronizes selection state.
        /// Used by rectangle selection in MainWindow.
        /// </summary>
        public void SynchronizeSelectedClips()
        {
            _selectedClips.Clear();
            foreach (var clip in TimelineClips.Where(c => c.IsSelected))
            {
                _selectedClips.Add(clip);
            }
            
            SelectedClip = _selectedClips.LastOrDefault();
            
            // Update command states
            GroupSelectedClipsCommand.NotifyCanExecuteChanged();
            UngroupSelectedClipsCommand.NotifyCanExecuteChanged();
            SeparateAudioCommand.NotifyCanExecuteChanged();
        }

        private bool CanMoveTo(TimelineClipBase clipToMove, double newStartPosition, int newTrackIndex, IEnumerable<TimelineClipBase> groupOfMovingClips)
        {
            // 타임라인 시작(0초) 이전으로는 이동 불가
            if (newStartPosition < 0) return false;

            // 이동하려는 클립의 예상 시간 범위
            double newEndPosition = newStartPosition + clipToMove.Duration;

            // 검사 대상: 전체 타임라인 클립 중에서, 현재 '함께 이동 중인 클립들'을 제외한 나머지 모든 클립
            var otherClips = TimelineClips.Except(groupOfMovingClips);

            // 목표 트랙에 있는 다른 클립들과 충돌하는지 검사
            foreach (var existingClip in otherClips.Where(c => c.TrackIndex == newTrackIndex))
            {
                // 시간 범위 (겹치는지 확인)
                double existingClipEnd = existingClip.StartPosition + existingClip.Duration;

                // 겹치는 조건:
                // 1. 새 클립의 시작이 기존 클립의 끝보다 전이고,
                // 2. 새 클립의 끝이 기존 클립의 시작보다 후일 때
                if (newStartPosition < existingClipEnd && newEndPosition > existingClip.StartPosition)
                {
                    return false; // 겹치므로 이동 불가
                }
            }

            return true; // 겹치는 클립이 없으므로 이동 가능
        }

        private double GetTimelineEndOfTrack(int trackIndex, IEnumerable<TimelineClipBase> clipsToExclude)
        {
            // clipsToExclude를 제외한 나머지 클립들 중에서
            return TimelineClips
                .Except(clipsToExclude)
                .Where(c => c.TrackIndex == trackIndex) // 해당 트랙에 있는 클립만 필터링
                .Select(c => c.StartPosition + c.Duration) // 각 클립의 끝나는 시간 계산
                .DefaultIfEmpty(0.0) // 만약 트랙이 비어있다면 기본값 0.0 사용
                .Max(); // 그중 가장 큰 값(가장 늦게 끝나는 시간)을 반환
        }

        public void Dispose()
        {
            _libVLC?.Dispose();
        }

    }
}
