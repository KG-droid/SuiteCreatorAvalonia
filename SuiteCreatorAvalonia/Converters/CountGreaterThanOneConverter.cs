using Avalonia.Data.Converters;
using System;

namespace SuiteCreatorAvalonia.Converters
{
    public class CountGreaterThanOneConverter : IValueConverter
    {
        public static readonly CountGreaterThanOneConverter Instance = new();

        public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return value is int count && count > 1;
        }

        public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}
