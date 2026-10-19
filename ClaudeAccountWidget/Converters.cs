using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace ClaudeAccountWidget;

/// <summary>전환 단계 점: 현재 단계 이상이면 강조색, 아니면 흐린색을 돌려줍니다.</summary>
public sealed class StepToBrushConverter : IValueConverter
{
    public Brush ActiveBrush { get; set; } = Brushes.Coral;
    public Brush InactiveBrush { get; set; } = new SolidColorBrush(Color.FromArgb(0x30, 0xFF, 0xFF, 0xFF));

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var step = value is int current ? current : 0;
        var index = int.TryParse(parameter?.ToString(), out var parsed) ? parsed : 0;
        return step >= index ? ActiveBrush : InactiveBrush;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
