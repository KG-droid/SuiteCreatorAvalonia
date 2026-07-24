using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.ViewModels;
using System;

namespace SuiteCreatorAvalonia.Views;

public partial class PackageOtherRemoveDetailsView : UserControl
{
    public PackageOtherRemoveDetailsView()
    {
        InitializeComponent();
    }

    private void RemoveType_SelectedItemChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBx)
        {
            OtherRemovalType removalType;
            if (Enum.TryParse<OtherRemovalType>(comboBx.SelectedValue.ToString(), true, out removalType))
            {
                RemoveMSI_Grid.IsVisible = false;
                RemoveRegex_Grid.IsVisible = false;
                RemoveCMD_Control.IsVisible = false;
                AddRemoveAnotherCMD_Button.IsVisible = false;
                RemovePS_Panel.IsVisible = false;
                Detection_Expander.IsVisible = removalType != OtherRemovalType.RegEx;
                switch (removalType)
                {
                    case OtherRemovalType.CMD:
                        RemoveCMD_Control.IsVisible = true;
                        AddRemoveAnotherCMD_Button.IsVisible = true;
                        break;
                    case OtherRemovalType.MSI:
                        RemoveMSI_Grid.IsVisible = true;
                        if (DataContext is PackageOtherRemoveDetailsViewModel vm && vm.RemoveMSIs.Count < 1)
                        {
                            vm.RemoveMSIs.Add(new MSIRemoveItemViewModel { IsProductRemoval = false });
                        }
                        break;
                    case OtherRemovalType.RegEx:
                        RemoveRegex_Grid.IsVisible = true;
                        break;
                    case OtherRemovalType.PowerShell:
                        RemovePS_Panel.IsVisible = true;
                        break;
                }
            }
        }
    }

    private void MSIRemoveDGCtrl_GotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (sender is Control textBox)
            MSIRemoves_DataGrid.SelectedItem = textBox.DataContext;
    }
}