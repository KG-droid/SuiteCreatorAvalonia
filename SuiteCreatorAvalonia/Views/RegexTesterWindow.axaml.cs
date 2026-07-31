using Avalonia.Controls;
using Avalonia.Interactivity;

namespace SuiteCreatorAvalonia.Views;

public partial class RegexTesterWindow : Window
{
    public RegexTesterWindow()
    {
        InitializeComponent();
    }

    private void CloseWindow(object sender, RoutedEventArgs e)
    {
        Close(false);
    }

    private void UsePattern(object sender, RoutedEventArgs e)
    {
        Close(true);
    }
}
