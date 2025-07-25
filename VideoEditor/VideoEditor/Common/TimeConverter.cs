using System;
using System.Globalization;
using System.Windows.Data;

namespace VideoEditor.Common
{
    public class TimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long milliseconds)
            {
                TimeSpan timeSpan = TimeSpan.FromMilliseconds(milliseconds);
                // 1시간 이상이면 hh:mm:ss, 아니면 mm:ss 형식으로 표시
                if (timeSpan.TotalHours >= 1)
                {
                    return timeSpan.ToString(@"hh\:mm\:ss");
                }
                else
                {
                    return timeSpan.ToString(@"mm\:ss");
                }
            }
            return "00:00"; // 기본값
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            // 이 시나리오에서는 역변환이 필요하지 않습니다. (UI 텍스트를 밀리초로)
            throw new NotImplementedException();
        }
    }
}