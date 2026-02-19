using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace RcloneMountManager.Converters;

public class BoolToBrushConverter : IValueConverter
{
    public object? TrueBrush { get; set; }
    public object? FalseBrush { get; set; }

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is true ? TrueBrush : FalseBrush;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
