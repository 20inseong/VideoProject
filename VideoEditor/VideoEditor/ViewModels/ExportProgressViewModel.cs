using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;
using VideoEditor.Common;

namespace VideoEditor.ViewModels
{
    public class ExportProgressViewModel : ViewModelBase
    {
        public ICommand CancelCommand { get; }
        public ExportProgressViewModel(Action cancelAction)
        {
            CancelCommand = new CommunityToolkit.Mvvm.Input.RelayCommand(cancelAction);
        }

        private string _statusMessage = "준비 중...";
        public string StatusMessage
        {
            get => _statusMessage;
            set => SetProperty(ref _statusMessage, value);
        }

        private double _progress;
        public double Progress
        {
            get => _progress;
            set => SetProperty(ref _progress, value);
        }
    }
}
