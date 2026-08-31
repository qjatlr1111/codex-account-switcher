using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace CodexAccountWidget;

internal static class EntranceAnimation
{
    private static readonly Duration AnimationDuration = TimeSpan.FromMilliseconds(140);

    public static void Play(Window window, double offset = 10)
    {
        var targetTop = window.Top;
        window.Opacity = 1;
        window.BeginAnimation(Window.TopProperty, CreateAnimation(targetTop + offset, targetTop));
        window.BeginAnimation(UIElement.OpacityProperty, CreateAnimation(0, 1));
    }

    public static void Play(FrameworkElement element, double offset = 8, bool fade = true)
    {
        var translation = element.RenderTransform as TranslateTransform ?? new TranslateTransform();
        element.RenderTransform = translation;
        element.Opacity = 1;
        translation.BeginAnimation(TranslateTransform.YProperty, CreateAnimation(offset, 0));
        element.BeginAnimation(
            UIElement.OpacityProperty,
            fade ? CreateAnimation(0, 1) : null);
    }

    private static DoubleAnimation CreateAnimation(double from, double to) => new(from, to, AnimationDuration)
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        FillBehavior = FillBehavior.Stop
    };
}
