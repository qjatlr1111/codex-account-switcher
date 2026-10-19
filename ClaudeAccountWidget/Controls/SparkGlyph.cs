using System.Windows;
using System.Windows.Media;

namespace ClaudeAccountWidget.Controls;

/// <summary>
/// 위젯 아이콘으로 쓰는 방사형 스파크 문양입니다. 길고 짧은 광선을 번갈아 그립니다.
/// 회전·크기 연출은 XAML의 RenderTransform으로 처리합니다.
/// </summary>
public sealed class SparkGlyph : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(SparkGlyph),
        new FrameworkPropertyMetadata(Brushes.Coral, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty RayCountProperty = DependencyProperty.Register(
        nameof(RayCount), typeof(int), typeof(SparkGlyph),
        new FrameworkPropertyMetadata(10, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public int RayCount { get => (int)GetValue(RayCountProperty); set => SetValue(RayCountProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var center = new Point(ActualWidth / 2, ActualHeight / 2);
        var outer = size / 2;
        var thickness = Math.Max(1.2, size * 0.13);
        var pen = new Pen(Fill, thickness) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };
        var rays = Math.Max(4, RayCount);

        for (var index = 0; index < rays; index++)
        {
            var angle = (index * 360d / rays - 90) * Math.PI / 180;
            var length = index % 2 == 0 ? outer - thickness / 2 : (outer - thickness / 2) * 0.68;
            var inner = size * 0.08;
            var from = new Point(center.X + inner * Math.Cos(angle), center.Y + inner * Math.Sin(angle));
            var to = new Point(center.X + length * Math.Cos(angle), center.Y + length * Math.Sin(angle));
            drawingContext.DrawLine(pen, from, to);
        }
    }
}
