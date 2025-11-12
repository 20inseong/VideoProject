using System.Windows;
using System.Windows.Documents;
using VideoEditor.Models;
using System.Linq;

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
                if ((bool)e.NewValue)
                {
                    if (adornedElement.IsLoaded)
                    {
                        AddAdorner(adornedElement);
                    }
                    else
                    {
                        RoutedEventHandler? loadedHandler = null;
                        loadedHandler = (sender, args) =>
                        {
                            adornedElement.Loaded -= loadedHandler;
                            AddAdorner(adornedElement);
                        };
                        adornedElement.Loaded += loadedHandler;
                    }
                }
                else
                {
                    RemoveAdorner(adornedElement);
                }
            }
        }

        private static void AddAdorner(FrameworkElement adornedElement)
        {
            AdornerLayer? adornerLayer = AdornerLayer.GetAdornerLayer(adornedElement);
            if (adornerLayer == null)
            {
                adornedElement.Dispatcher.BeginInvoke(new System.Action(() =>
                {
                    AdornerLayer? retryLayer = AdornerLayer.GetAdornerLayer(adornedElement);
                    if (retryLayer != null && adornedElement.DataContext is TimelineClipBase clip)
                    {
                        var existingAdorners = retryLayer.GetAdorners(adornedElement);
                        if (existingAdorners == null || !existingAdorners.OfType<ClipAdorner>().Any())
                        {
                            retryLayer.Add(new ClipAdorner(adornedElement, clip));
                        }
                    }
                }), System.Windows.Threading.DispatcherPriority.Loaded);
                return;
            }

            if (adornedElement.DataContext is TimelineClipBase clipContext)
            {
                var existingAdorners = adornerLayer.GetAdorners(adornedElement);
                if (existingAdorners == null || !existingAdorners.OfType<ClipAdorner>().Any())
                {
                    adornerLayer.Add(new ClipAdorner(adornedElement, clipContext));
                }
            }
        }

        private static void RemoveAdorner(FrameworkElement adornedElement)
        {
            AdornerLayer? adornerLayer = AdornerLayer.GetAdornerLayer(adornedElement);
            if (adornerLayer == null) return;

            Adorner[]? adorners = adornerLayer.GetAdorners(adornedElement);
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