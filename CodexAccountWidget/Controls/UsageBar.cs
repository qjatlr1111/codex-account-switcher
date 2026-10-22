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
/// 둥근 막대형 잔여량 표시기입니다. 채움 위에 얇은 하이라이트 줄과 끝부분 광점을 그려
/// 유리 같은 질감을 줍니다.
/// </summary>
public sealed class UsageBar : AnimatedMeter
{
    protected override Size MeasureOverride(Size availableSize) =>
        new(double.IsInfinity(availableSize.Width) ? 60 : availableSize.Width, 5);

    protected override void OnRender(DrawingContext drawingContext)
    {
        var width = ActualWidth;
        var height = ActualHeight;
        if (width <= 0 || height <= 0) return;

        var radius = height / 2;
        var track = new Rect(0, 0, width, height);

        if (IsUnknown)
        {
            var dashPen = new Pen(TrackBrush, height)
            {
                DashStyle = new DashStyle([0.1, 2.4], 0),
                DashCap = PenLineCap.Round
            };
            drawingContext.DrawLine(dashPen, new Point(radius, radius), new Point(width - radius, radius));
            return;
        }

        drawingContext.DrawRoundedRectangle(TrackBrush, null, track, radius, radius);

        var fraction = Math.Clamp(Value / 100d, 0, 1);
        var fillWidth = Math.Max(fraction > 0 ? height : 0, width * fraction);
        if (fillWidth <= 0) return;

        var fillRect = new Rect(0, 0, fillWidth, height);
        drawingContext.DrawRoundedRectangle(ActiveFill, null, fillRect, radius, radius);

        // 위쪽 하이라이트
        var shine = new SolidColorBrush(Color.FromArgb(0x55, 0xFF, 0xFF, 0xFF));
        shine.Freeze();
        drawingContext.DrawRoundedRectangle(shine, null,
            new Rect(radius * 0.6, height * 0.12, Math.Max(0, fillWidth - radius * 1.2), height * 0.28), 1, 1);

        // 끝부분 광점
        var glow = new RadialGradientBrush(Color.FromArgb(0xC0, 0xE6, 0xF6, 0xFF), Color.FromArgb(0, 0xE6, 0xF6, 0xFF));
        glow.Freeze();
        drawingContext.DrawEllipse(glow, null, new Point(fillWidth - radius, radius), height * 1.4, height * 1.4);
    }
}
