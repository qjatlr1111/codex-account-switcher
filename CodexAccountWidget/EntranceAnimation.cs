using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

// 이 프로젝트는 System.Drawing, System.Windows.Forms 전역 using이 켜져 있어 WPF 형식을 별칭으로 고정합니다.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace CodexAccountWidget;

internal static class EntranceAnimation
{
    private static readonly Duration ShortDuration = TimeSpan.FromMilliseconds(160);

    /// <summary>창 전체: 아래에서 떠오르며 페이드 인.</summary>
    public static void Play(Window window, double offset = 10)
    {
        var targetTop = window.Top;
        window.Opacity = 1;
        window.BeginAnimation(Window.TopProperty, Create(targetTop + offset, targetTop, ShortDuration));
        window.BeginAnimation(UIElement.OpacityProperty, Create(0, 1, ShortDuration));
    }

    /// <summary>요소: 아래에서 떠오르며 페이드 인.</summary>
    public static void Play(FrameworkElement element, double offset = 8, bool fade = true)
    {
        var translation = EnsureTransform<TranslateTransform>(element);
        element.Opacity = 1;
        translation.BeginAnimation(TranslateTransform.YProperty, Create(offset, 0, ShortDuration));
        element.BeginAnimation(UIElement.OpacityProperty, fade ? Create(0, 1, ShortDuration) : null);
    }

    /// <summary>
    /// 패널 등장: 살짝 작은 크기에서 BackEase로 튕기듯 커지며 위로 떠오릅니다.
    /// </summary>
    public static void PlayPop(FrameworkElement element, double offset = 14)
    {
        element.RenderTransformOrigin = new Point(0.12, 1);
        var group = new TransformGroup();
        var scale = new ScaleTransform(1, 1);
        var translate = new TranslateTransform();
        group.Children.Add(scale);
        group.Children.Add(translate);
        element.RenderTransform = group;
        // Opacity 애니메이션은 끝나면 기본값으로 돌아가므로 기본값을 먼저 1로 둡니다.
        element.Opacity = 1;

        var duration = new Duration(TimeSpan.FromMilliseconds(360));
        var back = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, duration) { EasingFunction = back });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.9, 1, duration) { EasingFunction = back });
        translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(offset, 0, duration) { EasingFunction = back });
        element.BeginAnimation(UIElement.OpacityProperty, Create(0, 1, TimeSpan.FromMilliseconds(200)));
    }

    /// <summary>목록 항목을 순서대로 하나씩 떠오르게 합니다.</summary>
    public static void PlayStaggered(IEnumerable<FrameworkElement> elements, int stepMilliseconds = 45, int startMilliseconds = 90)
    {
        var index = 0;
        foreach (var element in elements)
        {
            var delay = TimeSpan.FromMilliseconds(startMilliseconds + stepMilliseconds * index++);
            var translation = EnsureTransform<TranslateTransform>(element);
            var duration = new Duration(TimeSpan.FromMilliseconds(320));
            var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

            element.Opacity = 0;
            translation.BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, duration) { BeginTime = delay, EasingFunction = ease, FillBehavior = FillBehavior.Stop });
            element.BeginAnimation(UIElement.OpacityProperty,
                new DoubleAnimation(0, 1, duration) { BeginTime = delay, EasingFunction = ease });
        }
    }

    private static T EnsureTransform<T>(UIElement element) where T : Transform, new()
    {
        if (element.RenderTransform is T existing && !existing.IsFrozen) return existing;
        var created = new T();
        element.RenderTransform = created;
        return created;
    }

    private static DoubleAnimation Create(double from, double to, Duration duration) => new(from, to, duration)
    {
        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
        FillBehavior = FillBehavior.Stop
    };
}
