using System.Windows;
using System.Windows.Documents;
using VideoEditor.Models;

namespace VideoEditor.Common.Adorners
{
    public static class AdornerService
    {
        public static readonly DependencyProperty IsAdornerEnabledProperty =
            DependencyProperty.RegisterAttached(
                "IsAdornerEnabled",
                typeof(bool),
                typeof(AdornerService),
                new PropertyMetadata(false, OnIsAdornerEnabledChanged));

        public static bool GetIsAdornerEnabled(UIElement element)
        {
            return (bool)element.GetValue(IsAdornerEnabledProperty);
        }

        public static void SetIsAdornerEnabled(UIElement element, bool value)
        {
            element.SetValue(IsAdornerEnabledProperty, value);
        }

        private static void OnIsAdornerEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is FrameworkElement adornedElement)
            {
                AdornerLayer adornerLayer = AdornerLayer.GetAdornerLayer(adornedElement);
                if (adornerLayer == null) return;

                if ((bool)e.NewValue)
                {
                    // Adorner enabled, add it
                    if (adornedElement.DataContext is TimelineClipBase clip)
                    {
                        adornerLayer.Add(new ClipAdorner(adornedElement, clip));
                    }
                }
                else
                {
                    // Adorner disabled, remove it
                    Adorner[]? adorners = AdornerLayer.GetAdornerLayer(adornedElement)?.GetAdorners(adornedElement);
                    if (adorners != null)
                    {
                        foreach (Adorner adorner in adorners)
                        {
                            if (adorner is ClipAdorner)
                            {
                                adornerLayer.Remove(adorner);
                            }
                        }
                    }
                }
            }
        }
    }
}