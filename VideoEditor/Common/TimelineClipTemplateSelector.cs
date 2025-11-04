using System.Windows;
using System.Windows.Controls;
using VideoEditor.Models;

namespace VideoEditor.Common
{
    public class TimelineClipTemplateSelector : DataTemplateSelector
    {
        public DataTemplate? VideoClipTemplate { get; set; }
        public DataTemplate? AudioClipTemplate { get; set; }
        public DataTemplate? ImageClipTemplate { get; set; }
        public DataTemplate? TextClipTemplate { get; set; }

        public override DataTemplate SelectTemplate(object item, DependencyObject container)
        {
            if (item is VideoClip)
            {
                return VideoClipTemplate;
            }
            if (item is AudioClip)
            {
                return AudioClipTemplate;
            }
            if (item is ImageClip)
            {
                return ImageClipTemplate;
            }
            if (item is TextClip)
            {
                return TextClipTemplate;
            }

            return base.SelectTemplate(item, container);
        }
    }
}