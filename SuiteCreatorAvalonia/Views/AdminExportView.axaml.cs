using Avalonia.Controls;
using Avalonia.Styling;
using AvaloniaEdit.Highlighting;

namespace SuiteCreatorAvalonia.Views;

public partial class AdminExportView : UserControl
{
    public AdminExportView()
    {
        InitializeComponent();
        ThemeVariant themeVariant = App.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
        GlobalPopupCondition_TextEditor.SyntaxHighlighting = themeVariant == ThemeVariant.Light
            ? HighlightingManager.Instance.GetDefinition("PowerShellLight")
            : HighlightingManager.Instance.GetDefinition("PowerShellDark");
    }
}
