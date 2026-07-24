using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using SuiteCreatorAvalonia.ViewModels.RuleBuilder;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuiteCreatorAvalonia.Views.RuleBuilder;

public partial class RuleBuilderView : UserControl
{
    public RuleBuilderView()
    {
        InitializeComponent();
        int buttonCount = RuleTypePicker_Canvas.Children.Count();
        double radius = 45;
        double angleStep = 360 / buttonCount;
        double startAngle = 270;
        double size = radius * 2.5;
        RuleTypePicker_Canvas.Width = size;
        RuleTypePicker_Canvas.Height = size;
        double center = RuleTypePicker_Canvas.Width / 2;
        Canvas.SetLeft(RuleTypePicker_Canvas.Children[0], center - (RuleTypePicker_Canvas.Children[0].Width / 2));
        Canvas.SetTop(RuleTypePicker_Canvas.Children[0], center - (RuleTypePicker_Canvas.Children[0].Height / 2));
        for (int i = 1; i < buttonCount; i++)
        {
            RuleTypePicker_Canvas.Children[i].Width = 10;
            RuleTypePicker_Canvas.Children[i].Height = 10;
            double angleInRadians = ((angleStep * i) + startAngle) * (Math.PI / 180); // Convert angle to radians
            double x = (center + (radius * Math.Cos(angleInRadians))) - (RuleTypePicker_Canvas.Children[i].Height / 2);
            double y = (center + (radius * Math.Sin(angleInRadians))) - (RuleTypePicker_Canvas.Children[i].Height / 2);
            Canvas.SetLeft(RuleTypePicker_Canvas.Children[i], x);
            Canvas.SetTop(RuleTypePicker_Canvas.Children[i], y);
            RuleTypePicker_Canvas.Children[i].IsVisible = true;
        }
    }

    private void AdditionTypePicker_PointerMove(object? sender, PointerEventArgs e)
    {
        PointerPoint controlPoint = e.GetCurrentPoint(this);
        PixelPoint screenPoint = this.PointToScreen(new Point(controlPoint.Position.X, controlPoint.Position.Y));
        double maxScaleAmount = 3;
        int maxDistanceApart = 40;
        IEnumerable<Button> selectors = RuleTypePicker_Canvas.Children.OfType<Button>().Skip(1);
        foreach (var item in RuleTypePicker_Canvas.Children.Skip(1))
        {
            if (item is Button btn)
            {
                btn.ZIndex = 1;
                PixelPoint buttonPoint = btn.PointToScreen(new Point(btn.Width / 2, btn.Height / 2));
                double distanceBetween = Math.Sqrt(Math.Pow((screenPoint.X - buttonPoint.X), 2) + Math.Pow((screenPoint.Y - buttonPoint.Y), 2));
                // Resize the button and keep it centered (resizes work from top, not center by default)
                double newHeightWidth;
                if (distanceBetween <= maxDistanceApart)
                {
                    double scale = 1 + ((-maxScaleAmount / maxDistanceApart) * distanceBetween) + maxScaleAmount;
                    if (scale > 3.5) { scale = 3.5; }
                    newHeightWidth = 15 * scale;
                }
                else
                {
                    // Default size for buttons not near the cursor
                    newHeightWidth = 15;
                }
                double widthDiff = newHeightWidth - btn.Width;
                double heightDiff = newHeightWidth - btn.Height;
                btn.Width = newHeightWidth;
                btn.Height = newHeightWidth;
                Canvas.SetLeft(btn, Canvas.GetLeft(btn) - (widthDiff / 2));
                Canvas.SetTop(btn, Canvas.GetTop(btn) - (heightDiff / 2));
            }
        }
        Button mostHighlightedButton = selectors.Where(b => b.Width == selectors.Max(x => x.Width)).First();
        mostHighlightedButton.ZIndex = 2;
        switch (mostHighlightedButton.Tag)
        {
            case "Registry":
                PickerDescription_TextBlock.Text = "Add a Registry Rule";
                break;
            case "File":
                PickerDescription_TextBlock.Text = "Add a File Rule";
                break;
            case "MSI":
                PickerDescription_TextBlock.Text = "Add an MSI Rule";
                break;
            case "MSIx":
                PickerDescription_TextBlock.Text = "Add an MSIx Rule";
                break;
            case "PowerShell":
                PickerDescription_TextBlock.Text = "Add a PowerShell Rule";
                break;
            case "Group":
                PickerDescription_TextBlock.Text = "Add a Group start/end";
                break;
            case "OR":
                PickerDescription_TextBlock.Text = "Add an OR operator";
                break;
        }
    }

    private void AdditionTypePicker_PointerExited(object sender, PointerEventArgs e)
    {
        if (DataContext is RuleBuilderViewModel model)
        {
            model.IsRuleTypePickerOpen = false;
        }
        foreach (var item in RuleTypePicker_Canvas.Children.Skip(1))
        {
            if (item is Button btn)
            {
                btn.ZIndex = 1;
                // Resize the button and keep it centered (resizes work from top, not center by default)
                double newHeightWidth = 15;
                double widthDiff = newHeightWidth - btn.Width;
                double heightDiff = newHeightWidth - btn.Height;
                btn.Width = newHeightWidth;
                btn.Height = newHeightWidth;
                Canvas.SetLeft(btn, Canvas.GetLeft(btn) - (widthDiff / 2));
                Canvas.SetTop(btn, Canvas.GetTop(btn) - (heightDiff / 2));
            }
        }
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        SuiteConditionBuilderClear_Button.Flyout?.Hide();
        if (DataContext is RuleBuilderViewModel model)
        {
            model.RemoveAllItems();
        }
    }
}