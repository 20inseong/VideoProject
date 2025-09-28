// ViewModels/EditorHostViewModel.cs
using CommunityToolkit.Mvvm.Input;
using System.Windows.Input;
using VideoEditor.Common;
using VideoEditor.Models;

namespace VideoEditor.ViewModels
{
    public class EditorHostViewModel : ViewModelBase
    {
        private ViewModelBase? _currentEditor;
        public ViewModelBase? CurrentEditor
        {
            get => _currentEditor;
            private set => SetProperty(ref _currentEditor, value);
        }

        // ✨ [수정] PlayerViewModel을 public 속성으로 만들어 외부에서 접근 가능하게 합니다.
        public PlayerViewModel PlayerViewModel { get; }
        public VideoEditorViewModel VideoEditorViewModel { get; }

        public ICommand ShowClipEditorCommand { get; }
        public ICommand ShowSpeedEditorCommand { get; }
        public ICommand ShowEmotionEditorCommand { get; }

        public EditorHostViewModel(PlayerViewModel playerViewModel, VideoEditorViewModel videoEditorViewModel)
        {
            // ✨ [수정] 전달받은 ViewModel들을 public 속성에 할당합니다.
            PlayerViewModel = playerViewModel;
            VideoEditorViewModel = videoEditorViewModel;

            // 커맨드 초기화
            ShowClipEditorCommand = new RelayCommand(() => CurrentEditor = VideoEditorViewModel);
            ShowSpeedEditorCommand = new RelayCommand(() => CurrentEditor = PlayerViewModel);
            ShowEmotionEditorCommand = new RelayCommand(() => { /* 나중에 EmotionViewModel 할당 */ });

            VideoEditorViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(VideoEditorViewModel.SelectedClip))
                {
                    CurrentEditor = VideoEditorViewModel;
                }
            };

            CurrentEditor = VideoEditorViewModel;
        }
    }
}