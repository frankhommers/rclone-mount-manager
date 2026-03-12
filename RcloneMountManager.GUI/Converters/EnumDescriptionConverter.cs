using System;
using System.ComponentModel;
using System.Globalization;
using System.Reflection;
using Avalonia.Data.Converters;

namespace RcloneMountManager.GUI.Converters;

public class EnumDescriptionConverter : IValueConverter
{
  public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    if (value is not Enum enumValue)
    {
      return value?.ToString();
    }

    FieldInfo? field = enumValue.GetType().GetField(enumValue.ToString());
    DescriptionAttribute? attribute = field?.GetCustomAttribute<DescriptionAttribute>();
    return attribute?.Description ?? enumValue.ToString();
  }

  public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
  {
    throw new NotSupportedException();
  }
}