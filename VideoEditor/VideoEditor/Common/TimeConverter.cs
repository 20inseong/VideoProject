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
            throw new NotImplementedException();
        }

        public class TimeToPixelConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is double seconds && App.Current.MainWindow is MainWindow mainWindow)
                {
                    var viewModel = mainWindow.DataContext as MainViewModel;
                    if (viewModel != null)
                    {
                        return seconds * viewModel.VideoEditor.PixelsPerSecond;
                    }
                }
                return 0.0;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }
        public class TrackToPositionConverter : IValueConverter
        {
            private const double TrackHeight = 60.0;

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value is int trackIndex)
                {
                    return trackIndex * TrackHeight;
                }
                return 0.0;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            {
                throw new NotImplementedException();
            }
        }
    }
}