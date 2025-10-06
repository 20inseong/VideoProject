using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace VideoEditor.Common
{
    public class ViewModelBase : INotifyPropertyChanged        //데이터가 변경되었음을 알리는데 사용되는 인터페이스
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
        protected bool SetProperty<T>(ref T backingStore, T value, [CallerMemberName] string propertyName = "")
        {
            // 값이 이미 같다면 아무것도 하지 않음
            if (EqualityComparer<T>.Default.Equals(backingStore, value))
                return false;

            // 값이 다르다면 실제 변수(backingStore)의 값을 업데이트
            backingStore = value;

            // UI에 변경 사실을 알림 (OnPropertyChanged 호출)
            OnPropertyChanged(propertyName);

            // 값이 변경되었음을 알림
            return true;
        }
    }
}
