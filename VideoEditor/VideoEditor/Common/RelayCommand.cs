using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;

namespace VideoEditor.Common
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Func<bool>? _canExecute; // CanExecute를 위한 Func 추가

        public RelayCommand(Action<object?> execute, Func<bool>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        // CanExecute 상태가 바뀔 수 있음을 UI에 알리는 이벤트
        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        // CanExecute 로직이 있으면 실행하고, 없으면 항상 true 반환
        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute();

        public void Execute(object? parameter) => _execute(parameter);

        // 외부에서 CanExecute 상태를 갱신하도록 강제하는 메서드
        public void RaiseCanExecuteChanged()
        {
            CommandManager.InvalidateRequerySuggested();
        }
    }
}
