using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using VideoEditor.Models;
using VideoEditor.ViewModels;

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

    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return !b;
            }
            return value; // Return original value if not a boolean
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is bool b)
            {
                return !b;
            }
            return value; // Return original value if not a boolean
        }
    }

    public class NullToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            return value == null ? Visibility.Collapsed : Visibility.Visible;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class XYToMarginConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimelineClipBase clip)
            {
                return new Thickness(clip.X, clip.Y, 0, 0);
            }
            return new Thickness(0);
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class InverseTrackIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is int trackIndex)
            {
                return -trackIndex;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class IsAudioClipInGroupConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            // values[0]은 GroupId, values[1]은 전체 TimelineClips 컬렉션
            if (values.Length < 2 || values[0] == null || values[1] == null || values[0] == DependencyProperty.UnsetValue)
            {
                return false;
            }

            if (values[0] is Guid groupId && values[1] is ObservableCollection<TimelineClipBase> allClips)
            {
                // 같은 GroupId를 가진 클립들 중에서 AudioClip 타입이 하나라도 있는지 확인
                return allClips.Any(clip => clip.GroupId == groupId && clip is AudioClip);
            }

            return false;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class ClipZIndexConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is TimelineClipBase clip)
            {
                // Base Z-index: lower track index = higher Z-index to appear on top
                int baseZ = -clip.TrackIndex * 10;

                if (clip is VideoClip)
                {
                    // Video clips get a penalty to appear behind other overlays on the same track
                    return baseZ - 5;
                }
                else // ImageClips, TextClips, etc.
                {
                    return baseZ;
                }
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}