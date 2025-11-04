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

        public PlayerViewModel PlayerViewModel { get; }
        public VideoEditorViewModel VideoEditorViewModel { get; }

        public ICommand ShowClipEditorCommand { get; }
        public ICommand ShowSpeedEditorCommand { get; }
        public ICommand ShowEmotionEditorCommand { get; }

        public EditorHostViewModel(PlayerViewModel playerViewModel, VideoEditorViewModel videoEditorViewModel)
        {
            PlayerViewModel = playerViewModel;
            VideoEditorViewModel = videoEditorViewModel;

            ShowClipEditorCommand = new RelayCommand(() => CurrentEditor = VideoEditorViewModel);
            ShowSpeedEditorCommand = new RelayCommand(() => CurrentEditor = PlayerViewModel);
            ShowEmotionEditorCommand = new RelayCommand(() => { /* 나중에 EmotionViewModel 할당 */ });

            VideoEditorViewModel.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(VideoEditorViewModel.SelectedClip))
                {
                    CurrentEditor = VideoEditorViewModel.SelectedClip;
                }
            };

            CurrentEditor = null;
        }
    }
}