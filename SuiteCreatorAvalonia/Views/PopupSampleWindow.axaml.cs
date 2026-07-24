using Avalonia;
using Avalonia.Controls;
using System;

namespace SuiteCreatorAvalonia.Views;

public partial class PopupSampleWindow : Window
{
    public PopupSampleWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    private void Popup_LayoutUpdated(object? sender, EventArgs e)
    {
        SetWindowPosition();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        SetWindowPosition();
    }

    private void SetWindowPosition()
    {
        if (Screens.Primary is { } screen)
        {
            // Get the working area of the primary screen (in physical pixels)
            var workingArea = screen.WorkingArea;

            // Get the scaling factor (defaults to 1.0 if not available)
            var scaling = screen.Scaling;

            // Convert the window's current bounds (in DIPs) to physical pixels
            var windowPixelSize = PixelSize.FromSize(this.Bounds.Size, scaling);

            // Calculate the position such that the window is aligned to the bottom right
            this.Position = new PixelPoint(
                workingArea.Right - (windowPixelSize.Width + 5),
                workingArea.Bottom - (windowPixelSize.Height + 5)
            );
        }
    }
}