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
/// 위젯 아이콘으로 쓰는 터미널 프롬프트(">_") 문양입니다.
/// CursorOpacity로 커서 깜빡임을, ChevronOffset으로 꺾쇠가 앞으로 튀어나가는 연출을 줍니다.
/// </summary>
public sealed class PromptGlyph : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(
        nameof(Fill), typeof(Brush), typeof(PromptGlyph),
        new FrameworkPropertyMetadata(Brushes.CornflowerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty CursorOpacityProperty = DependencyProperty.Register(
        nameof(CursorOpacity), typeof(double), typeof(PromptGlyph),
        new FrameworkPropertyMetadata(1d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ChevronOffsetProperty = DependencyProperty.Register(
        nameof(ChevronOffset), typeof(double), typeof(PromptGlyph),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush Fill { get => (Brush)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public double CursorOpacity { get => (double)GetValue(CursorOpacityProperty); set => SetValue(CursorOpacityProperty, value); }
    public double ChevronOffset { get => (double)GetValue(ChevronOffsetProperty); set => SetValue(ChevronOffsetProperty, value); }

    protected override void OnRender(DrawingContext drawingContext)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0) return;

        var thickness = Math.Max(1.3, size * 0.15);
        var left = (ActualWidth - size) / 2;
        var top = (ActualHeight - size) / 2;
        var pen = new Pen(Fill, thickness)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };

        // 꺾쇠 ">"
        var shift = ChevronOffset * size;
        var chevron = new StreamGeometry();
        using (var context = chevron.Open())
        {
            context.BeginFigure(new Point(left + size * 0.14 + shift, top + size * 0.24), false, false);
            context.LineTo(new Point(left + size * 0.44 + shift, top + size * 0.5), true, true);
            context.LineTo(new Point(left + size * 0.14 + shift, top + size * 0.76), true, true);
        }
        chevron.Freeze();
        drawingContext.DrawGeometry(null, pen, chevron);

        // 커서 "_"
        if (CursorOpacity <= 0.01) return;
        drawingContext.PushOpacity(Math.Clamp(CursorOpacity, 0, 1));
        drawingContext.DrawLine(pen,
            new Point(left + size * 0.56, top + size * 0.76),
            new Point(left + size * 0.88, top + size * 0.76));
        drawingContext.Pop();
    }
}
