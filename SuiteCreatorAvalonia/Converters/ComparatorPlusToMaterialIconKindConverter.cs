using Avalonia.Data;
using Avalonia.Data.Converters;
using Material.Icons;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Rules;
using System;
using System.Linq;

public class ComparatorPlusToMaterialIconKindConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is ComparatorPlus comparator)
        {
            return ComparatorMapping.ComparatorToIconKind[comparator];
        }
        return new BindingNotification(new Exception($"Unable to match Type: {value?.GetType().Name}, to a MaterialIconKind"), BindingErrorType.Error);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is MaterialIconKind iconKind)
        {
            ComparatorPlus? comparator = ComparatorMapping.ComparatorToIconKind.Where(c => c.Value == iconKind).First().Key;
            if (comparator == null)
            {
                return new BindingNotification(new Exception($"Unable to to match MaterialIconKind: {iconKind.ToString()}, to a ComparatorPlus"), BindingErrorType.Error);
            }
            return comparator;
        }
        return new BindingNotification(new Exception($"Unable to match Type: {value?.GetType().Name}, to a ComparatorPlus"), BindingErrorType.Error);
    }
}