using Avalonia.Data;
using Avalonia.Data.Converters;
using Material.Icons;
using SuiteCreatorAvalonia.Enums;
using System;

public class ArchitectureToMaterialIconConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is Architecture arch)
        {
            if (arch is Architecture.x86)
                return MaterialIconKind.Cpu32Bit;
            else if (arch is Architecture.x64)
                return MaterialIconKind.Cpu64Bit;
            else
                return new BindingNotification(new Exception($"Unable to match Architecture: {arch.ToString()}, to a MaterialIconKind"), BindingErrorType.Error);
        }
        return new BindingNotification(new Exception($"Cannot convert Type: {value?.GetType().Name}, it is not an Architecture"), BindingErrorType.Error);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is MaterialIconKind iconKind)
        {
            if (iconKind == MaterialIconKind.Cpu32Bit)
                return Architecture.x86;
            else if (iconKind == MaterialIconKind.Cpu64Bit)
                return Architecture.x64;
            else
                return new BindingNotification(new Exception($"Unable to match MaterialIconKind: {iconKind}, to an Architecture"), BindingErrorType.Error);
        }
        return new BindingNotification(new Exception($"Cannot convert Type: {value?.GetType().Name}, as it is not a MaterialKind"), BindingErrorType.Error);
    }
}