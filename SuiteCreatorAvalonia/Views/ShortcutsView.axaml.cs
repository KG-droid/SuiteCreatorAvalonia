using Avalonia.Controls;
using SuiteCreatorAvalonia.ViewModels;

namespace SuiteCreatorAvalonia.Views;

public partial class ShortcutsView : UserControl
{
    public ShortcutsView()
    {
        InitializeComponent();
    }

    private void ClearConfirmButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is ShortcutsViewModel sVM)
        {
            sVM.RemoveShortcuts(true);
        }
        ClearAll_Button.Flyout?.Hide();
    }
}