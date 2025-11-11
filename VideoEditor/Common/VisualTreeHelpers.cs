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
        
        public static T? FindVisualChild<T>(this DependencyObject parent) where T : DependencyObject
        {
            if (parent == null) return null;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                
                if (child is T typedChild)
                {
                    return typedChild;
                }

                var result = FindVisualChild<T>(child);
                if (result != null)
                {
                    return result;
                }
            }

            return null;
        }
    }
}