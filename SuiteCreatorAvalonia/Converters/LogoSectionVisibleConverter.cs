using Avalonia.Data.Converters;
using System;
using System.Collections.Generic;
using System.Globalization;

namespace SuiteCreatorAvalonia.Converters
{
    // Values: [0] ShowProgress checkbox IsChecked, [1] ShowPopupWarning checkbox IsChecked,
    // [2] the Popups TabControl's SelectedIndex. The shared Company/Suite Logo section only
    // applies to the Warning Popup (index 0) and Progress Popup (index 1) tabs, not the
    // Popups Launch Condition tab (index 2).
    internal class LogoSectionVisibleConverter : IMultiValueConverter
    {
        private const int ConditionTabIndex = 2;

        public object? Convert(
            IList<object?> values,
            Type targetType,
            object? parameter,
            CultureInfo culture)
        {
            bool showProgress = values.Count > 0 && values[0] is true;
            bool showWarning = values.Count > 1 && values[1] is true;
            int selectedIndex = values.Count > 2 && values[2] is int index ? index : -1;

            return (showProgress || showWarning) && selectedIndex != ConditionTabIndex;
        }
    }
}
