using System;
using System.Globalization;
using System.Windows.Data;
using VideoEditor.ViewModels;

namespace VideoEditor.Common
{
    public class TimeConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long milliseconds)
            {
                TimeSpan timeSpan = TimeSpan.FromMilliseconds(milliseconds);
                return timeSpan.ToString(@"hh\:mm\:ss\.ff");
            }
            return "00:00:00";
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string timeString)
            {
                if (TimeSpan.TryParse(timeString, out TimeSpan timeSpan))
                {
                    return (long)timeSpan.TotalMilliseconds;
                }
            }
            return 0L;
        }

    }
}