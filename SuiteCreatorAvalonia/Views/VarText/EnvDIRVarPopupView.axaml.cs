using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Styling;
using SuiteCreatorAvalonia.ViewModels;

namespace SuiteCreatorAvalonia.Views;

public partial class EnvDIRVarPopupView : Popup
{
    public EnvDIRVarPopupView()
    {
        InitializeComponent();
        ThemeVariantScope.RequestedThemeVariant = App.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
        Variables_ListBox.AddHandler(InputElement.PointerReleasedEvent, SelectedVariable_PointerPressed, RoutingStrategies.Tunnel | RoutingStrategies.Bubble, true);
    }

    private void SelectedVariable_PointerPressed(object? sender, PointerReleasedEventArgs e)
    {
        if (this.DataContext is EnvDIRVarPopupViewModel eVM && Variables_ListBox.SelectedItem != null)
        {
            TriggerSelectionChanged();
        }
    }

    private void TriggerSelectionChanged()
    {
        if (this.DataContext is EnvDIRVarPopupViewModel eVM && Variables_ListBox.SelectedItem != null)
        {
            string variableName = Variables_ListBox.SelectedItem.ToString();
            Variables_ListBox.SelectedItem = null;
            eVM.SelectVariable(variableName);
        }
    }
}