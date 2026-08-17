using Avalonia;
using Avalonia.Controls;
using Avalonia.Platform;
using SuiteProgressPopup.ViewModels;
using System;

namespace SuiteProgressPopup.Views;

public partial class LockdownWindow : Window
{
    private Screen? _targetScreen;

    public LockdownWindow()
    {
        InitializeComponent();
        Opened += OnOpened;
    }

    // Pins this window instance to exactly cover the given monitor, rather than relying on the
    // OS-native fullscreen transition, which only ever targets a single, ambiguous "current"
    // screen. Used for the extra blocker windows spawned on every screen besides the primary one
    // during lockdown, so a multi-monitor setup can't be worked around by switching monitors.
    // Must be called before Show().
    public void SetTargetScreen(Screen screen)
    {
        _targetScreen = screen;
        ApplyTargetScreenBounds();
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // The primary instance has no explicit target set upfront (it's shown as the app's
        // MainWindow before any Screens data is guaranteed to be available), so fall back to
        // whichever screen it actually landed on once it has a real platform handle.
        _targetScreen ??= Screens.Primary;
        ApplyTargetScreenBounds();

        // Built fresh per window rather than bound to a shared ViewModel property - a Control can
        // only belong to one visual tree at a time, and every monitor gets its own window here.
        if (DataContext is ProgressWindowViewModel viewModel)
        {
            CompanyLogo_ContentControl.Content = viewModel.CreateCompanyLogoControl();
        }
    }

    private void ApplyTargetScreenBounds()
    {
        if (_targetScreen is null)
            return;

        double scaling = _targetScreen.Scaling;
        PixelRect bounds = _targetScreen.Bounds;

        WindowStartupLocation = WindowStartupLocation.Manual;
        Width = bounds.Width / scaling;
        Height = bounds.Height / scaling;
        Position = new PixelPoint(bounds.X, bounds.Y);
    }
}
