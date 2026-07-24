using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteCreatorAvalonia.ViewModels.RuleBuilder;
using System.Collections.ObjectModel;
using System.Linq;
namespace SuiteCreatorAvalonia.Views.RuleBuilder;

public partial class RuleCardView : UserControl
{
    DataFormat<RuleBase> _ruleDF = DataFormat.CreateInProcessFormat<RuleBase>("Rule");

    public RuleCardView()
    {
        InitializeComponent();
    }

    private void RuleDragOver(object? sender, DragEventArgs e)
    {
        if (
            e.DataTransfer.Items.OfType<DataTransferItem>().FirstOrDefault(item => item.Formats.Contains(_ruleDF))?.TryGetRaw(_ruleDF) is RuleBase sourceRuleBase &&
            sender is RuleCardView dropDestinationCardView &&
            dropDestinationCardView.DataContext is RuleCardBaseViewModel destRuleBaseVM &&
            destRuleBaseVM.LinkedRule != null &&
            sourceRuleBase != destRuleBaseVM.LinkedRule &&
            dropDestinationCardView.FindAncestorOfType<ItemsControl>() is ItemsControl parentItemsControl &&
            parentItemsControl.ItemsSource is ObservableCollection<RuleBase> items
        )
        {
            items.Move(items.IndexOf(sourceRuleBase), items.IndexOf(destRuleBaseVM.LinkedRule));
        }
    }

    private async void RulePointerPressed(object sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(sender as Control);
        if (
            point.Properties.IsLeftButtonPressed &&
            this.DataContext is RuleCardBaseViewModel ruleBaseVM &&
            ruleBaseVM.LinkedRule != null
        )
        {
            DataTransfer dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.Create(_ruleDF, ruleBaseVM.LinkedRule));

            ThemeVariant? themeVariant = App.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
            if (themeVariant != null && App.Current != null && App.Current.FindResource(themeVariant, "SystemAccentColor") is Color color)
            {
                CardMainBorder.Background = new SolidColorBrush(color);
                DragDrop.DoDragDropAsync(e, dataTransfer, DragDropEffects.Move);
            }
        }
    }

    private void Card_Drop(object? sender, DragEventArgs e)
    {
        CardMainBorder.ClearValue(Border.BackgroundProperty); // Clear the local value to let the style take over
    }

    public void RemoveCard(object? sender, RoutedEventArgs e)
    {
        if (this.FindAncestorOfType<UserControl>()?.DataContext is RuleBuilderViewModel vm)
        {
            //vm.Rules.Remove(this);
        }
    }
}