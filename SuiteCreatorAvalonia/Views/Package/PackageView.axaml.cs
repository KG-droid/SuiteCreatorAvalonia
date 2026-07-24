using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Material.Icons.Avalonia;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.ViewModels;
using System;
using System.Linq;

namespace SuiteCreatorAvalonia.Views;

public partial class PackageView : UserControl
{
    private readonly DataFormat<PackageBase> _packageDF = DataFormat.CreateInProcessFormat<PackageBase>("PackageBase");

    public PackageView()
    {
        InitializeComponent();
        AddPackageButtons();
    }

    private async void PackageItem_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(sender as Control);
        if (
            point.Properties.IsLeftButtonPressed &&
            sender is Control iconCtrl &&
            iconCtrl.DataContext is PackageBase pkg
        )
        {
            DataTransfer dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.Create(_packageDF, pkg));
            await DragDrop.DoDragDropAsync(e, dataTransfer, DragDropEffects.Move);
        }
    }

    private void PackageItem_DragOver(object? sender, DragEventArgs e)
    {
        if (
            e.DataTransfer.Items.OfType<DataTransferItem>().FirstOrDefault(item => item.Formats.Contains(_packageDF))?.TryGetRaw(_packageDF) is PackageBase sourcePkg &&
            sender is Control destControl &&
            destControl.DataContext is PackageBase destPkg &&
            sourcePkg != destPkg &&
            this.DataContext is PackageViewModel pVM &&
            pVM.PackageList.Contains(sourcePkg) &&
            pVM.PackageList.Contains(destPkg)
        )
        {
            int oldIndex = pVM.PackageList.IndexOf(sourcePkg);
            int newIndex = pVM.PackageList.IndexOf(destPkg);
            if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
            {
                pVM.PackageList.Move(oldIndex, newIndex);
            }
        }
    }

    private void PackageItem_Drop(object? sender, DragEventArgs e)
    {
        if (this.DataContext is PackageViewModel pVM)
        {
            pVM.SaveOrder();
        }
    }

    private void AddPackage_KeyUp(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddNewPackage(sender);
        }
    }

    private void AddPackageButton_Click(object? sender, RoutedEventArgs e)
    {
        AddNewPackage(sender);
    }

    private void AddNewPackage(object? sender)
    {
        if (string.IsNullOrWhiteSpace(NewPackageName_TextBox.Text))
        {
            NewPackageName_TextBox.Text = "Untitled Package";
        }
        if (sender is Control ctrl)
        {
            Button? pkgTypeBtn = ctrl.FindLogicalAncestorOfType<Button>();
            if (pkgTypeBtn == null) { return; }
            if (Enum.TryParse(pkgTypeBtn.Tag?.ToString(), out PackageType pkgType))
            {
                if (this.DataContext is PackageViewModel pVM)
                {
                    pVM.AddPackageByType(NewPackageName_TextBox.Text, pkgType);
                }
                pkgTypeBtn.Flyout?.Hide();
                Button? addPkgButton = pkgTypeBtn.FindLogicalAncestorOfType<Button>();
                if (addPkgButton == null) { return; }
                addPkgButton.Flyout?.Hide();
                NewPackageName_TextBox.Text = string.Empty;
            }
        }
    }

    private void RemovePackageButton_Click(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (sender is Button pkgNameBtn && this.DataContext is PackageViewModel pVM && pVM.SelectedPackage != null)
        {
            pVM.RemoveSelectedPackage();
            Button? pkgTypeBtn = pkgNameBtn.FindLogicalAncestorOfType<Button>();
            if (pkgTypeBtn == null) { return; }
            pkgTypeBtn.Flyout?.Hide();
            Button? addPkgButton = pkgTypeBtn.FindLogicalAncestorOfType<Button>();
            if (addPkgButton == null) { return; }
            addPkgButton.Flyout?.Hide();
        }
    }

    private void AddPackageButtons()
    {
        AddPackageButtons_StackPanel.Children.Clear();
        Flyout packageNameFlyout = (Flyout)this.FindResource("PackageNameFlyout");
        foreach (PackageTypeListItem pkgTypeItem in PackageTypeList.PackageTypes)
        {
            Button pkgTypeBtn = new()
            {
                Content = new Grid
                {
                    Children =
                    {
                        new Image
                        {
                            Source = new Bitmap(AssetLoader.Open(new Uri(pkgTypeItem.IconPath))),
                            Width = 25,
                            Height = 25,
                        },
                        new MaterialIcon
                        {
                            Kind = Material.Icons.MaterialIconKind.Close,
                            Foreground = Avalonia.Media.Brushes.Red,
                            IsVisible = pkgTypeItem.IsRemoval,
                            Width = 25,
                            Height = 25,
                        }
                    }
                },
                Flyout = packageNameFlyout,
                Tag = pkgTypeItem.Name,
            };
            ToolTip.SetTip(pkgTypeBtn, pkgTypeItem.Name);
            ToolTip.SetShowDelay(pkgTypeBtn, 0);
            AddPackageButtons_StackPanel.Children.Add(pkgTypeBtn);
        }
    }
}