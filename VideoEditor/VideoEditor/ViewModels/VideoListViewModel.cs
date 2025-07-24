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
                    // 선택된 비디오가 변경될 때 어떤 동작을 수행할 수 있습니다.
                    // 예를 들어, 메인 ViewModel에 이 변경을 알릴 수 있습니다.
                    // 이 예시에서는 메인 ViewModel에서 이 속성을 구독할 예정입니다.
                }
            }
        }

        // 비디오 추가를 위한 Command (옵션: 직접 파일을 열어 추가하는 경우)
        public ICommand AddVideoCommand { get; }

        public VideoListViewModel()
        {
            MyVideoes = new ObservableCollection<Myvideo>();
            // AddVideoCommand = new RelayCommand(ExecuteAddVideo); // 필요한 경우 주석 해제하여 사용
        }

        // 비디오 아이템을 목록에 추가하는 메서드 (외부에서 호출될 수 있음)
        public void AddVideo(Myvideo videoItem)
        {
            MyVideoes.Add(videoItem);
        }

        // AddVideoCommand를 위한 실행 메서드 (필요한 경우)
        // private void ExecuteAddVideo(object? parameter)
        // {
        //     // 파일 다이얼로그를 열어 비디오 선택 후 목록에 추가하는 로직
        //     // 이 예시에서는 MainWindow에서 파일 선택 로직을 직접 처리하므로 사용하지 않습니다.
        // }
    }
}
