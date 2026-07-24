using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SuiteCreatorAvalonia.Converters
{
    internal class AnyTrueConverter : IMultiValueConverter
    {
        public object? Convert(
        IList<object?> values,
        Type targetType,
        object? parameter,
        CultureInfo culture)
        {
            foreach (var value in values)
            {
                if (value is true)
                    return true;
            }
            return false;
        }

    }
}
