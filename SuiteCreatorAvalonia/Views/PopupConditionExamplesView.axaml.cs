using Avalonia.Controls;
using Avalonia.Styling;
using AvaloniaEdit.Highlighting;

namespace SuiteCreatorAvalonia.Views;

public partial class PopupConditionExamplesView : UserControl
{
    public PopupConditionExamplesView()
    {
        InitializeComponent();

        ThemeVariant themeVariant = App.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
        IHighlightingDefinition? highlighting = themeVariant == ThemeVariant.Light
            ? HighlightingManager.Instance.GetDefinition("PowerShellLight")
            : HighlightingManager.Instance.GetDefinition("PowerShellDark");

        GlobalOnly_Global_Editor.SyntaxHighlighting = highlighting;
        GlobalOnly_Popup_Editor.SyntaxHighlighting = highlighting;
        PopupOnly_Global_Editor.SyntaxHighlighting = highlighting;
        PopupOnly_Popup_Editor.SyntaxHighlighting = highlighting;
        Both_Global_Editor.SyntaxHighlighting = highlighting;
        Both_Popup_Editor.SyntaxHighlighting = highlighting;
    }
}
