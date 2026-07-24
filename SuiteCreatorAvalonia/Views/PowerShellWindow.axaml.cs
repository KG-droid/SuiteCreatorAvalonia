using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using AvaloniaEdit.Highlighting;
using SuiteCreatorAvalonia.ViewModels;
using System;

namespace SuiteCreatorAvalonia.Views;

public partial class PowerShellWindow : Window
{
    public PowerShellWindow()
    {
        InitializeComponent();

        // Load the custom syntax highlighting for the texteditor
        ThemeVariant themeVariant = App.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
        if (themeVariant == ThemeVariant.Light)
        {
            Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("PowerShellLight");
        }
        else
        {
            Editor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("PowerShellDark");
        }
    }
    private void ClosePSWindow(object sender, RoutedEventArgs e)
    {
        if (DataContext is PowerShellWindowViewModel pVM)
        {
            bool scriptNameMissing = !pVM.IsRuleMode && string.IsNullOrEmpty(pVM.ScriptName);
            bool scriptBodyMissing = string.IsNullOrEmpty(pVM.ScriptDoc.Text) && string.IsNullOrEmpty(pVM.FilePath);
            if (scriptNameMissing || scriptBodyMissing)
            {
                string warningMessage;
                if (scriptNameMissing)
                {
                    warningMessage = "You have not provided a name for the script, if you close this window, changes will not be saved, are you sure you want to close?";
                }
                else
                {
                    warningMessage = "You have not provided a script or script file, if you close this window, changes will not be saved, are you sure you want to close?";
                }
                Flyout warningFlyout = new();
                warningFlyout.Placement = PlacementMode.Center;
                StackPanel warningFlyoutStackPanel = new();
                warningFlyoutStackPanel.Margin = new(0, 0, 0, 10);
                StackPanel warningFlyoutButtonStackPanel = new();
                warningFlyoutButtonStackPanel.Orientation = Orientation.Horizontal;
                warningFlyoutButtonStackPanel.HorizontalAlignment = HorizontalAlignment.Center;
                TextBlock warningText = new()
                {
                    Text = warningMessage,
                    Margin = new(15),
                    TextWrapping = TextWrapping.Wrap,
                    MaxWidth = 400
                };
                Button yesButton = new()
                {
                    Content = "Yes",
                    Margin = new(5, 0, 5, 0)
                };
                yesButton.Click += (s, args) =>
                {
                    warningFlyout.Hide();
                    Close();
                };
                Button noButton = new()
                {
                    Content = "No",
                    Margin = new(5, 0, 5, 0)
                };
                noButton.Click += (s, args) =>
                {
                    warningFlyout.Hide();
                };
                warningFlyoutButtonStackPanel.Children.Add(yesButton);
                warningFlyoutButtonStackPanel.Children.Add(noButton);
                warningFlyoutStackPanel.Children.Add(warningText);
                warningFlyoutStackPanel.Children.Add(warningFlyoutButtonStackPanel);
                warningFlyout.Content = warningFlyoutStackPanel;
                warningFlyout.ShowAt(this);
            }
            else
            {
                Close();
            }
        }
        else
        {
            throw new InvalidOperationException("DataContext is not of type PowerShellWindowViewModel, so cannot perform field validation");
        }
    }
}
