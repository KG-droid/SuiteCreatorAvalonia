using Avalonia;
using Avalonia.Controls;
using System;

namespace SuiteUserPopup.Views;

public partial class PopupWindow : Window
{
    // When true, centres the window on screen instead of the usual bottom-right toast position —
    // used for blocked-process notices so the user can't miss them.
    public bool CenterOnScreen { get; set; }

    public PopupWindow()
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
            var scaling = screen?.Scaling ?? 1.0;

            // Convert the window's current bounds (in DIPs) to physical pixels
            var windowPixelSize = PixelSize.FromSize(this.Bounds.Size, scaling);

            this.Position = CenterOnScreen
                ? new PixelPoint(
                    workingArea.X + (workingArea.Width - windowPixelSize.Width) / 2,
                    workingArea.Y + (workingArea.Height - windowPixelSize.Height) / 2)
                // Calculate the position such that the window is aligned to the bottom right
                : new PixelPoint(
                    workingArea.Right - (windowPixelSize.Width + 5),
                    workingArea.Bottom - (windowPixelSize.Height + 5));
        }
    }
}