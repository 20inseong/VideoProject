using System;
using System.Windows.Threading;

namespace VideoEditor.Common
{
    public static class UIDispatcher
    {
        private static Dispatcher? _dispatcher;

        // 앱 시작 시 딱 한 번만 호출될 초기화 메서드
        public static void Initialize()
        {
            // 현재(UI) 스레드의 Dispatcher를 저장
            _dispatcher = Dispatcher.CurrentDispatcher;
        }

        // UI 스레드에서 특정 액션을 실행하도록 요청하는 메서드
        public static void Invoke(Action action)
        {
            _dispatcher?.Invoke(action);
        }
    }
}