using System.Windows;
using System.Windows.Media;

namespace VideoEditor.Common
{
    public static class VisualTreeHelpers
    {
        public static T? FindAncestor<T>(this DependencyObject current) where T : DependencyObject
        {
            if (current == null) return null;

            do
            {
                if (current is T ancestor)
                {
                    return ancestor;
                }
                current = VisualTreeHelper.GetParent(current);
            }
            while (current != null);

            return null;
        }
    }
}