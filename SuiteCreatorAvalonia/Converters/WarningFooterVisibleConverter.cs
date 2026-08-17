using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SuiteCreatorAvalonia.Converters
{
    // Values: [0] SuitePopupEnable checkbox IsChecked, [1] the Popups TabControl's SelectedIndex.
    // The advisory footer text only applies to the Warning Popup tab (index 0).
    internal class WarningFooterVisibleConverter : IMultiValueConverter
    {
        private const int WarningTabIndex = 0;

        public object? Convert(
            IList<object?> values,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            bool showWarning = values.Count > 0 && values[0] is true;
            int selectedIndex = values.Count > 1 && values[1] is int index ? index : -1;

            return showWarning && selectedIndex == WarningTabIndex;
        }
    }
}
