// Common/Converters.cs

using System;
using System.Globalization;
using System.Windows.Data;
using VideoEditor.ViewModels;
using VideoEditor.Models;

namespace VideoEditor.Common
{
    public class TimeToPixelConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double seconds && App.Current.MainWindow is MainWindow mainWindow)
            {
                if (mainWindow.DataContext is MainViewModel viewModel)
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
    public class TypeToBoolConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value == null || parameter == null || !(parameter is Type target))
            {
                return false;
            }
            return value.GetType() == target || value.GetType().IsSubclassOf(target);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
    public class ClipTypeToStringConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value switch
            {
                VideoClip => "비디오 클립",
                ImageClip => "이미지 클립",
                AudioClip => "오디오 클립",
                TextClip => "자막 클립",
                _ => "알 수 없는 클립"
            };
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}