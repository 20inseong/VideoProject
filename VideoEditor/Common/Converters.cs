using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;
using System.Windows.Media;
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
                // Return trackIndex as-is so higher track numbers appear on top
                // Track 0 = ZIndex 0 (bottom), Track 8 = ZIndex 8 (top)
                return trackIndex;
            }
            return 0;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Converter that sets Z-Index based on TrackIndex and Selection state
    /// Selected clips get a boost to appear on top
    /// </summary>
    public class OverlayZIndexConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2)
                return 0;

            int trackIndex = 0;
            bool isSelected = false;

            if (values[0] is int ti)
                trackIndex = ti;
            
            if (values[1] is bool sel)
                isSelected = sel;

            // Base Z-Index: higher track index appears on top (Track 0 = bottom, Track 8 = top)
            int baseZ = trackIndex * 10;
            
            // If selected, add a modest boost to bring it above siblings on the same track
            // But keep it below 100 to avoid blocking UI elements
            if (isSelected)
                baseZ += 50;

            return baseZ;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
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
                    // Video clips get a BOOST to appear in front of other overlays on the same track
                    // This ensures video clips receive mouse events first in the timeline
                    return baseZ + 5;
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

    public class OpacityPercentToDecimalConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double percent)
            {
                return percent / 100.0;
            }
            return 1.0; // Default to fully opaque
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double decimal_value)
            {
                return decimal_value * 100.0;
            }
            return 100.0;
        }
    }

    public class ColorToBrushConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Color color)
            {
                return new SolidColorBrush(color);
            }
            return Brushes.Transparent;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class FontFamilyConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is string fontFamilyName)
            {
                try { return new FontFamily(fontFamilyName); }
                catch (Exception) { return SystemFonts.MessageFontFamily; }
            }
            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FontFamily fontFamily)
            {
                if (fontFamily.FamilyNames.TryGetValue(XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.Name), out string localizedName))
                {
                    return localizedName;
                }
                if (fontFamily.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("en-US"), out string englishName))
                {
                    return englishName;
                }
                return fontFamily.Source.Split(',').FirstOrDefault()?.Trim();
            }
            return Binding.DoNothing;
        }
    }

    public class HalfValueConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is double val) { return val / 2.0; }
            return 0;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }

    public class AbsoluteTimestampConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            //Debug.WriteLine($"[AbsoluteTimestampConverter] values[0]: {values[0]}, values[1]: {values[1]}");

            if (values.Length == 2 && values[0] is double startPosition && values[1] is double relativeTimestamp)
            {
                double result = startPosition + relativeTimestamp;
                //Debug.WriteLine($"[AbsoluteTimestampConverter] Result: {result}");
                return result;
            }

            //Debug.WriteLine("[AbsoluteTimestampConverter] Invalid values, returning 0.0");
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotImplementedException();
        }
    }
}