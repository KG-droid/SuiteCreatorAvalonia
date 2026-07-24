using Avalonia.Data;
using Avalonia.Data.Converters;
using Material.Icons;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Rules;
using System;
using System.Linq;

public class MaterialIconKindToComparatorPlusStringConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is MaterialIconKind iconKind)
        {
            ComparatorPlus? comparator = ComparatorMapping.ComparatorToIconKind.Where(c => c.Value == iconKind).First().Key;
            if (comparator == null)
            {
                return new BindingNotification(new Exception($"Unable to to match MaterialIconKind: {iconKind.ToString()}, to a ComparatorPlus"), BindingErrorType.Error);
            }
            return comparator.ToString();
        }
        return new BindingNotification(new Exception($"Unable to match Type: {value?.GetType().Name}, to a MaterialIconKind"), BindingErrorType.Error);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is string comparator)
        {
            if (Enum.TryParse(comparator, out ComparatorPlus ComparatorPlus))
            {
                return ComparatorMapping.ComparatorToIconKind[ComparatorPlus];
            }
            else
            {
                return new BindingNotification(new Exception($"Unable to to match MaterialIconKind string: {value}, to a ComparatorPlus"), BindingErrorType.Error);
            }
        }
        return new BindingNotification(new Exception($"Unable to match Type: {value?.GetType().Name}, to a ComparatorPlus"), BindingErrorType.Error);
    }
}