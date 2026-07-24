using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using System;
using System.Collections;
using System.Collections.Generic;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteCreatorAvalonia.ViewModels;
using SuiteCreatorAvalonia.ViewModels.EventCards;
using System.Collections;
using System.Linq;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.Views;

public partial class EventCardView : UserControl
{
    DataFormat<EventCore> _eventCoreDF = DataFormat.CreateInProcessFormat<EventCore>("EventCore");
    private readonly HashSet<ComboBox> _initializedComboBoxes = new();

    public EventCardView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _initializedComboBoxes.Clear();
    }

    private async void Schedule_Changed(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox)
        {
            // Condition binds OneWay because a bound ComboBox gets its selection cleared by Avalonia
            // when it's detached from the visual tree (e.g. navigating to another page), and that
            // clear would otherwise silently null out an already-chosen condition. A real user pick
            // always shows up as an added item; the detach-driven clear never adds anything, only
            // removes, so gating the write on AddedItems tells the two apart.
            bool committedCondition = false;
            if (comboBox.DataContext is Schedule schedule && e.AddedItems.Count > 0 && e.AddedItems[0] is RuleSet ruleSet)
            {
                schedule.Condition = ReferenceEquals(ruleSet, RuleSet.None) ? null : ruleSet;
                committedCondition = true;
            }

            // Normally the combo's first-ever SelectionChanged is just the initial binding sync, not a
            // user action, so it's skipped below. Condition starts with nothing selected though, so a
            // user's first real pick on it IS that box's first SelectionChanged; committedCondition
            // being true is proof it was a genuine pick, so save regardless of that first-time gate.
            bool isFirstFiring = !_initializedComboBoxes.Add(comboBox);
            if (isFirstFiring || committedCondition)
            {
                if (DataContext is EventCardViewModelBase eventCardVM && eventCardVM.LinkedEvent != null && e.AddedItems.Count > 0)
                {
                    eventCardVM.SaveEvent();
                }
            }
            if (
                comboBox.FindAncestorOfType<IOEventView>() is IOEventView parentIOView &&
                parentIOView.DataContext is IOEventViewModel ioVM
            )
            {
                ioVM.IsEventsSelectable = false;
                await Task.Delay(1000);
                ioVM.IsEventsSelectable = true;
            }
        }
    }

    private void Card_DragEnter(object? sender, DragEventArgs e)
    {
        if (DataContext is EventCardViewModelBase viewModel)
        {
            viewModel.IsSelected = false;
        }
    }

    private void Card_PointerEntered(object? sender, PointerEventArgs e)
    {
        if (
            DataContext is EventCardViewModelBase eVMBase &&
            sender is EventCardView eventCardView &&
            eventCardView.FindAncestorOfType<IOEventView>() is IOEventView parentIOView &&
            parentIOView.DataContext is IOEventViewModel ioVM &&
            ioVM.IsEventsSelectable
        )
        {
            eVMBase.IsSelected = true;
            DeselectOtherCards();
        }
    }

    private void Card_Drop(object? sender, DragEventArgs e)
    {
        CardMainBorder.ClearValue(Border.BackgroundProperty); // Clear the local value to let the style take over
        if (this.FindAncestorOfType<IOEventView>() is IOEventView parentIOView &&
            parentIOView.DataContext is IOEventViewModel ioVM)
        {
            ioVM.SaveOrder();
        }
    }

    private void Card_DragOver(object? sender, DragEventArgs e)
    {
        if (
            e.DataTransfer.Items.OfType<DataTransferItem>().FirstOrDefault(item => item.Formats.Contains(_eventCoreDF))?.TryGetRaw(_eventCoreDF) is EventCore sourceEventCore &&
            sender is EventCardView dropDestinationCardView &&
            dropDestinationCardView.DataContext is EventCardViewModelBase destEventBaseVM &&
            destEventBaseVM.LinkedEvent != null &&
            sourceEventCore != destEventBaseVM.LinkedEvent &&
            dropDestinationCardView.FindAncestorOfType<ItemsControl>() is ItemsControl parentItemsControl &&
            parentItemsControl.ItemsSource is IList list &&
            list.Contains(sourceEventCore) &&
            list.Contains(destEventBaseVM.LinkedEvent)
        )
        {
            int oldIndex = list.IndexOf(sourceEventCore);
            int newIndex = list.IndexOf(destEventBaseVM.LinkedEvent);
            if (oldIndex >= 0 && newIndex >= 0 && oldIndex != newIndex)
            {
                list.RemoveAt(oldIndex);
                list.Insert(newIndex, sourceEventCore);
            }
        }
    }

    private async void Card_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        PointerPoint point = e.GetCurrentPoint(sender as Control);
        EventCardView? parentEventCardView = (sender as Control).FindAncestorOfType<EventCardView>();
        if (
            point.Properties.IsLeftButtonPressed &&
            DataContext is EventCardViewModelBase viewModel &&
            viewModel.LinkedEvent != null &&
            parentEventCardView is EventCardView &&
            parentEventCardView.FindAncestorOfType<IOEventView>() is IOEventView parentIOView &&
            parentIOView.DataContext is IOEventViewModel ioVM &&
            ioVM.IsEventsSelectable
        )
        {
            viewModel.IsSelected = true;
            DeselectOtherCards();
            DataTransfer dataTransfer = new DataTransfer();
            dataTransfer.Add(DataTransferItem.Create(_eventCoreDF, viewModel.LinkedEvent));

            ThemeVariant? themeVariant = App.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
            if (themeVariant != null && App.Current != null && App.Current.FindResource(themeVariant, "SystemAccentColor") is Color color)
            {
                CardMainBorder.Background = new SolidColorBrush(color);
                await DragDrop.DoDragDropAsync(e, dataTransfer, DragDropEffects.Move);
            }
        }
    }

    private void DeselectOtherCards()
    {
        if (this.FindAncestorOfType<ItemsControl>() is ItemsControl parentItemsControl)
        {
            parentItemsControl.GetLogicalDescendants().OfType<EventCardView>().ToList().ForEach(x =>
            {
                if (x != this && x.DataContext is EventCardViewModelBase eventVM)
                    eventVM.IsSelected = false;
            });
        }
    }
}
