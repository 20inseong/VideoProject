using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using VideoEditor.Models;
using VideoEditor.Common;
using Wpf.Ui.Input;

namespace VideoEditor.ViewModels
{
    public class VideoListViewModel : ViewModelBase
    {
        private ObservableCollection<Myvideo> _myVideoes;
        public ObservableCollection<Myvideo> MyVideoes
        {
            get => _myVideoes;
            set => SetProperty(ref _myVideoes, value);
        }

        private Myvideo? _selectedVideoItem;
        public Myvideo? SelectedVideoItem
        {
            get => _selectedVideoItem;
            set
            {
                if (SetProperty(ref _selectedVideoItem, value))
                {
                    (DeleteSelectedVideoCommand as RelayCommand<object>)?.NotifyCanExecuteChanged();
                }
            }
        }

        public ICommand AddVideoCommand { get; }
        public ICommand DeleteSelectedVideoCommand { get; }

        public VideoListViewModel()
        {
            MyVideoes = new ObservableCollection<Myvideo>();
            DeleteSelectedVideoCommand = new RelayCommand<object>(ExecuteDeleteSelectedVideo, CanExecuteDeleteSelectedVideo);
        }

        private void ExecuteDeleteSelectedVideo(object? _)
        {
            if (SelectedVideoItem != null)
            {
                MyVideoes.Remove(SelectedVideoItem);
                SelectedVideoItem = null;
            }
        }

        private bool CanExecuteDeleteSelectedVideo(object? _)
        {
            return SelectedVideoItem != null;
        }


        public void AddVideo(Myvideo videoItem)
        {
            MyVideoes.Add(videoItem);
        }
    }
}
