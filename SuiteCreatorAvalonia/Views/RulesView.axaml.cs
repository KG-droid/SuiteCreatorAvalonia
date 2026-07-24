using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SuiteCreatorAvalonia.ViewModels;
using System;

namespace SuiteCreatorAvalonia.Views;

public partial class RulesView : UserControl
{
    public RulesView()
    {
        InitializeComponent();
    }

    private void AddRuleSet_Click(object sender, RoutedEventArgs e)
    {
        AddRule();
    }

    private void AddRuleSet_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return)
        {
            AddRule();
        }
    }

    private void AddRule()
    {
        if (DataContext is RulesViewModel vm)
        {
            if (NewName_TextBox.Text != null && (NewName_TextBox.Text.Contains('(') || NewName_TextBox.Text.Contains(')')))
            {
                AddWarning_Popup.IsOpen = true;
                return;
            }
            vm.AddRuleSet(NewName_TextBox.Text);
            NewName_TextBox.Text = string.Empty;
            NewRuleSet_button.Flyout?.Hide();
        }
    }

    private void RenameRuleSet_KeyUp(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Return && DataContext is RulesViewModel vm && !string.IsNullOrWhiteSpace(NewRuleName_Textbox.Text))
        {
            vm.EditRuleSetName(NewRuleName_Textbox.Text);
            EditName_button.Flyout?.Hide();
        }
    }

    private void RenameRuleSet_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RulesViewModel vm && !string.IsNullOrWhiteSpace(NewRuleName_Textbox.Text))
        {
            vm.EditRuleSetName(NewRuleName_Textbox.Text);
            EditName_button.Flyout?.Hide();
        }
    }

    private void RemoveRuleSet_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is RulesViewModel vm)
        {
            vm.RemoveSelectedRuleSet();
            if (string.IsNullOrWhiteSpace(vm.RuleRemoveError))
            {
                RemoveRuleSet_Button.Flyout?.Hide();
            }
        }
    }

    private void RemovalErrPopup_Closed(object? sender, EventArgs e)
    {
        if (DataContext is RulesViewModel vm)
        {
            vm.RuleRemoveError = null;
        }
    }
}