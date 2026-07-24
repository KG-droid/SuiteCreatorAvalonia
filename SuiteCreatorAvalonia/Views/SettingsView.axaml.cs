using Avalonia.Controls;
using Avalonia.Styling;
using AvaloniaEdit.Highlighting;
using System.Linq;
using System.Reflection;

namespace SuiteCreatorAvalonia.Views;

public partial class SettingsView : UserControl
{
    public SettingsView()
    {
        InitializeComponent();
        ThemeTypes_ComboBox.ItemsSource = typeof(ThemeVariant)
            .GetProperties(BindingFlags.Public | BindingFlags.Static)
            .Select(p => p.Name);

        ThemeVariant themeVariant = App.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
        GlobalPopupCondition_TextEditor.SyntaxHighlighting = themeVariant == ThemeVariant.Light
            ? HighlightingManager.Instance.GetDefinition("PowerShellLight")
            : HighlightingManager.Instance.GetDefinition("PowerShellDark");

        // The Ctrl+Shift+A admin-export shortcut (see KeyBindings below) only fires while this control or a
        // descendant has focus. The nav rail that navigates here isn't part of this UserControl's subtree, so
        // grab focus on show - otherwise the shortcut would only work after clicking something on the page first.
        AttachedToVisualTree += (_, _) => Focus();
    }
}
