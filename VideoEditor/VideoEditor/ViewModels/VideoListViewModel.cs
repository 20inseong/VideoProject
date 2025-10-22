using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using VideoEditor.Models;
using VideoEditor.Common;

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
                    // 선택된 비디오가 변경될 때 어떤 동작을 수행할 수 있음
                    // 예를 들어, 메인 ViewModel에 이 변경을 알릴 수 있음
                    // 이 예시에서는 메인 ViewModel에서 이 속성을 구독할 예정
                }
            }
        }

        public ICommand AddVideoCommand { get; }

        public VideoListViewModel()
        {
            MyVideoes = new ObservableCollection<Myvideo>();
        }

        public void AddVideo(Myvideo videoItem)
        {
            MyVideoes.Add(videoItem);
        }
    }
}
