using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.ViewModels;
using System;

namespace SuiteCreatorAvalonia.Views;

public partial class PackageOtherInstallDetailsView : UserControl
{
    public PackageOtherInstallDetailsView()
    {
        InitializeComponent();
        DataContextChanged += (s, e) =>
            {
                if (DataContext is PackageOtherInstallDetailsViewModel vm)
                {
                    ApplyInstallType(vm.SelectedInstallType);
                    ApplyRollbackType(vm.SelectedRollbackType);
                }
            };
    }

    private void ApplyInstallType(OtherInstallType installType)
    {
        InstallCMD_Control.IsVisible = installType == OtherInstallType.CMD;
        InstallPS_Panel.IsVisible = installType == OtherInstallType.PowerShell;
    }

    private void ApplyRollbackType(OtherInstallType rollbackType)
    {
        RollbackCMD_Control.IsVisible = rollbackType == OtherInstallType.CMD;
        RollbackPS_Panel.IsVisible = rollbackType == OtherInstallType.PowerShell;
    }

    private void InstallType_SelectedItemChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBx && comboBx.SelectedValue != null)
        {
            if (Enum.TryParse<OtherInstallType>(comboBx.SelectedValue.ToString(), true, out OtherInstallType installType))
                ApplyInstallType(installType);
        }
    }

    private void RollbackType_SelectedItemChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBx && comboBx.SelectedValue != null)
        {
            if (Enum.TryParse<OtherInstallType>(comboBx.SelectedValue.ToString(), true, out OtherInstallType rollbackType))
                ApplyRollbackType(rollbackType);
        }
    }

    private void RemoveType_SelectedItemChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBx)
        {
            if (Enum.TryParse<OtherRemovalType>(comboBx.SelectedValue?.ToString(), true, out OtherRemovalType removalType))
            {
                RemoveMSI_Grid.IsVisible = false;
                RemoveRegex_Grid.IsVisible = false;
                RemoveCMD_Control.IsVisible = false;
                RemovePS_Panel.IsVisible = false;
                AddRemoveAnotherCMD_Button.IsVisible = false;
                switch (removalType)
                {
                    case OtherRemovalType.CMD:
                        RemoveCMD_Control.IsVisible = true;
                        AddRemoveAnotherCMD_Button.IsVisible = true;
                        break;
                    case OtherRemovalType.MSI:
                        RemoveMSI_Grid.IsVisible = true;
                        if (DataContext is PackageOtherInstallDetailsViewModel vm && vm.RemoveMSIs.Count < 1)
                            vm.RemoveMSIs.Add(new MSIRemoveItemViewModel { IsProductRemoval = false });
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
        if (sender is Control ctrl)
            MSIRemoves_DataGrid.SelectedItem = ctrl.DataContext;
    }
}