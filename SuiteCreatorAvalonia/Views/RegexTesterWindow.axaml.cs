using Avalonia.Controls;
using Avalonia.Interactivity;
using SuiteCreatorAvalonia.ViewModels;

namespace SuiteCreatorAvalonia.Views;

public partial class RegexTesterWindow : Window
{
    public RegexTesterWindow()
    {
        InitializeComponent();

        DataContextChanged += (s, e) =>
        {
            if (DataContext is RegexTesterWindowViewModel vm)
            {
                SampleEditor.TextArea.TextView.LineTransformers.Add(new RegexMatchColorizer(() => vm.MatchRanges));
                vm.MatchesUpdated += () => SampleEditor.TextArea.TextView.Redraw();
            }
        };
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
