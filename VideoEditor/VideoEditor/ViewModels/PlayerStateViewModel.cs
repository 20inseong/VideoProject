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

        private double _opacity = 1.0;
        public double Opacity
        {
            get => _opacity;
            set => SetProperty(ref _opacity, value);
        }
    }
}