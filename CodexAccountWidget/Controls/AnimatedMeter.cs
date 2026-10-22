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

namespace CodexAccountWidget.Controls;

/// <summary>
/// 목표값(Target)이 바뀌면 표시값(Value)을 부드럽게 따라가게 하는 공통 미터 베이스입니다.
/// Target이 0보다 작으면 "알 수 없음" 상태로 그립니다.
/// </summary>
public abstract class AnimatedMeter : FrameworkElement
{
    public static readonly DependencyProperty TargetProperty = DependencyProperty.Register(
        nameof(Target), typeof(double), typeof(AnimatedMeter),
        new PropertyMetadata(-1d, OnTargetChanged));

    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(AnimatedMeter),
        new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty TrackBrushProperty = DependencyProperty.Register(
        nameof(TrackBrush), typeof(Brush), typeof(AnimatedMeter),
        new FrameworkPropertyMetadata(new SolidColorBrush(Color.FromArgb(0x2A, 0xFF, 0xFF, 0xFF)),
            FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty FillBrushProperty = DependencyProperty.Register(
        nameof(FillBrush), typeof(Brush), typeof(AnimatedMeter),
        new FrameworkPropertyMetadata(Brushes.CornflowerBlue, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LowBrushProperty = DependencyProperty.Register(
        nameof(LowBrush), typeof(Brush), typeof(AnimatedMeter),
        new FrameworkPropertyMetadata(Brushes.IndianRed, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty LowThresholdProperty = DependencyProperty.Register(
        nameof(LowThreshold), typeof(double), typeof(AnimatedMeter),
        new FrameworkPropertyMetadata(15d, FrameworkPropertyMetadataOptions.AffectsRender));

    public double Target { get => (double)GetValue(TargetProperty); set => SetValue(TargetProperty, value); }
    public double Value { get => (double)GetValue(ValueProperty); set => SetValue(ValueProperty, value); }
    public Brush TrackBrush { get => (Brush)GetValue(TrackBrushProperty); set => SetValue(TrackBrushProperty, value); }
    public Brush FillBrush { get => (Brush)GetValue(FillBrushProperty); set => SetValue(FillBrushProperty, value); }
    public Brush LowBrush { get => (Brush)GetValue(LowBrushProperty); set => SetValue(LowBrushProperty, value); }
    public double LowThreshold { get => (double)GetValue(LowThresholdProperty); set => SetValue(LowThresholdProperty, value); }

    protected bool IsUnknown => Target < 0;
    protected Brush ActiveFill => !IsUnknown && Target <= LowThreshold ? LowBrush : FillBrush;

    /// <summary>패널이 열릴 때 0에서부터 다시 차오르는 연출을 재생합니다.</summary>
    public void Replay(TimeSpan delay)
    {
        if (IsUnknown) return;
        BeginAnimation(ValueProperty, CreateAnimation(0, Target, delay, 820));
    }

    private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        var meter = (AnimatedMeter)d;
        var to = Math.Clamp((double)e.NewValue, 0, 100);
        if ((double)e.NewValue < 0) to = 0;
        var from = meter.Value;
        meter.BeginAnimation(ValueProperty, meter.IsLoaded
            ? CreateAnimation(from, to, TimeSpan.Zero, 700)
            : CreateAnimation(0, to, TimeSpan.FromMilliseconds(120), 900));
        meter.InvalidateVisual();
    }

    private static DoubleAnimation CreateAnimation(double from, double to, TimeSpan delay, int milliseconds) =>
        new(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            BeginTime = delay,
            EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut },
            FillBehavior = FillBehavior.HoldEnd
        };
}
