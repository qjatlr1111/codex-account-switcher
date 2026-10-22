using System.Windows;
using System.Windows.Media;

// 이 프로젝트는 System.Drawing, System.Windows.Forms 전역 using이 켜져 있어 WPF 형식을 별칭으로 고정합니다.
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;
using Pen = System.Windows.Media.Pen;
using Point = System.Windows.Point;
using Rect = System.Windows.Rect;
using Size = System.Windows.Size;

namespace CodexAccountWidget.Controls;

/// <summary>
/// 12시 방향에서 시계 방향으로 차오르는 원형 게이지입니다.
/// 같은 호를 넓고 흐린 펜으로 한 번 더 그려 은은한 블룸(glow)을 줍니다.
/// </summary>
public sealed class ArcGauge : AnimatedMeter
{
    public static readonly DependencyProperty StrokeThicknessProperty = DependencyProperty.Register(
        nameof(StrokeThickness), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(3d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty GlowOpacityProperty = DependencyProperty.Register(
        nameof(GlowOpacity), typeof(double), typeof(ArcGauge),
        new FrameworkPropertyMetadata(0.28d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double StrokeThickness { get => (double)GetValue(StrokeThicknessProperty); set => SetValue(StrokeThicknessProperty, value); }
    public double GlowOpacity { get => (double)GetValue(GlowOpacityProperty); set => SetValue(GlowOpacityProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var glowThickness = StrokeThickness * 2.6;
        var radius = (size - glowThickness) / 2;
        if (radius <= 0) return;
        var center = new Point(ActualWidth / 2, ActualHeight / 2);

        var trackPen = new Pen(TrackBrush, StrokeThickness);
        if (IsUnknown)
        {
            trackPen.DashStyle = new DashStyle([1, 2.2], 0);
            trackPen.DashCap = PenLineCap.Round;
        }
        drawingContext.DrawEllipse(null, trackPen, center, radius, radius);

        var fraction = Math.Clamp(Value / 100d, 0, 1);
        if (IsUnknown || fraction <= 0.002) return;

        var geometry = CreateArc(center, radius, fraction);
        var fill = ActiveFill;

        drawingContext.PushOpacity(GlowOpacity);
        drawingContext.DrawGeometry(null, new Pen(fill, glowThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        }, geometry);
        drawingContext.Pop();

        drawingContext.DrawGeometry(null, new Pen(fill, StrokeThickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        }, geometry);

        // 호 끝의 작은 하이라이트 점: 값이 움직일 때 혜성처럼 보이게 합니다.
        var endAngle = (-90 + 360 * fraction) * Math.PI / 180;
        var head = new Point(center.X + radius * Math.Cos(endAngle), center.Y + radius * Math.Sin(endAngle));
        drawingContext.DrawEllipse(Brushes.White, null, head, StrokeThickness * 0.32, StrokeThickness * 0.32);
    }

    private static Geometry CreateArc(Point center, double radius, double fraction)
    {
        if (fraction >= 0.999) return new EllipseGeometry(center, radius, radius);

        var startAngle = -Math.PI / 2;
        var endAngle = startAngle + 2 * Math.PI * fraction;
        var start = new Point(center.X + radius * Math.Cos(startAngle), center.Y + radius * Math.Sin(startAngle));
        var end = new Point(center.X + radius * Math.Cos(endAngle), center.Y + radius * Math.Sin(endAngle));

        var geometry = new StreamGeometry();
        using (var context = geometry.Open())
        {
            context.BeginFigure(start, isFilled: false, isClosed: false);
            context.ArcTo(end, new Size(radius, radius), 0, fraction > 0.5, SweepDirection.Clockwise, true, true);
        }

        geometry.Freeze();
        return geometry;
    }
}
