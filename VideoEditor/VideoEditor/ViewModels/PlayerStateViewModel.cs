using System.Windows.Media;
using VideoEditor.Common;

namespace VideoEditor.ViewModels
{
    public class PlayerStateViewModel : ViewModelBase
    {
        private Transform _transform = Transform.Identity;
        public Transform Transform
        {
            get => _transform;
            set => SetProperty(ref _transform, value);
        }
    }
}