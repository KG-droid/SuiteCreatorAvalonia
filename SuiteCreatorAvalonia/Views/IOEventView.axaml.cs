using Avalonia.Controls;
using SuiteCreatorAvalonia.ViewModels;

namespace SuiteCreatorAvalonia.Views;

public partial class IOEventView : UserControl
{
    public IOEventView()
    {
        InitializeComponent();
    }

    private void ClearConfirmButton_Click(object sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (DataContext is IOEventViewModel ioVM)
        {
            ioVM.DeleteCards(true);
        }
        ClearAll_Button.Flyout?.Hide();
    }
}
