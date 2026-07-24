using Avalonia.Controls;
using Avalonia.Interactivity;
using SuiteCreatorAvalonia.ViewModels;
using System;

namespace SuiteCreatorAvalonia.Views;

public partial class ShortcutThumbnailView : UserControl
{
    public ShortcutThumbnailView()
    {
        InitializeComponent();
        DataContextChanged += ShortcutsView_DataContextChanged;
    }

    private void ShortcutsView_DataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is ShortcutThumbnailViewModel sVM)
        {
            sVM.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(sVM.Target) || args.PropertyName == nameof(sVM.WorkingDIR) || args.PropertyName == nameof(sVM.IconPath))
                {
                    ShortThumb_Button.Flyout?.ShowAt(ShortThumb_Button);
                }
            };
        }
    }

    public void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (Parent is WrapPanel panel)
        {
            panel.Children.Remove(this);
        }
    }
}