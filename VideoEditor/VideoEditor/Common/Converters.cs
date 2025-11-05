using System;
using System.Collections.ObjectModel;
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
                try
                {
                    return new FontFamily(fontFamilyName);
                }
                catch (Exception)
                {
                    return SystemFonts.MessageFontFamily;
                }
            }
            return Binding.DoNothing;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is FontFamily fontFamily)
            {
                // --- ★★★ 핵심 수정 부분 ★★★ ---

                // 1. 현재 UI 문화권(한국어)에 맞는 이름을 우선적으로 찾습니다.
                if (fontFamily.FamilyNames.TryGetValue(XmlLanguage.GetLanguage(CultureInfo.CurrentUICulture.Name), out string localizedName))
                {
                    return localizedName;
                }

                // 2. 만약 한국어 이름이 없다면, '미국 영어(en-US)' 이름을 찾습니다.
                //    Wingdings 같은 심볼 폰트들은 보통 여기에 이름을 가지고 있습니다.
                if (fontFamily.FamilyNames.TryGetValue(XmlLanguage.GetLanguage("en-US"), out string englishName))
                {
                    return englishName;
                }

                // 3. 위 두 가지 방법으로도 이름을 찾지 못했다면, 최후의 수단으로 Source 속성을 사용합니다.
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
            // values[0] = Clip의 StartPosition (부모로부터)
            // values[1] = Emotion의 Timestamp (자신으로부터)

            // 두 값이 모두 double 타입인지 확인
            if (values.Length == 2 && values[0] is double startPosition && values[1] is double relativeTimestamp)
            {
                // 두 값을 더해서 절대 시간을 계산
                return startPosition + relativeTimestamp;
            }

            // 값이 유효하지 않으면 0을 반환
            return 0.0;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            // 이 기능에서는 사용하지 않으므로 구현할 필요 없음
            throw new NotImplementedException();
        }
    }
}