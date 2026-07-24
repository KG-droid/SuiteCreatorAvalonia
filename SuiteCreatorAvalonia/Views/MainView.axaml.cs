using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using SuiteCreatorAvalonia.ViewModels;
using System.ComponentModel;
namespace SuiteCreatorAvalonia.Views;

public partial class MainView : UserControl
{
    private Window? _subscribedWindow;
    private bool _autoRecoveryChecked = false;

    public MainView()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        AttachedToVisualTree += OnAttachedToVisualTree;
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        if (_autoRecoveryChecked) return;
        _autoRecoveryChecked = true;

        // Post to end of UI queue so DialogHost has fully registered before we try to show a dialog
        Dispatcher.UIThread.Post(() =>
        {
            if (DataContext is MainViewModel mVM)
                _ = mVM.CheckAutoRecoveryAsync();
        }, DispatcherPriority.Loaded);
    }

    private void OnLoaded(object? sender, RoutedEventArgs e)
    {
        Services.HelpService.Instance.PageHost = PageContentControl;
        Window? window = TopLevel.GetTopLevel(this) as Window;
        if (window != null && window != _subscribedWindow)
        {
            if (_subscribedWindow != null)
                _subscribedWindow.Closing -= OnWindowClosing;
            _subscribedWindow = window;
            _subscribedWindow.Closing += OnWindowClosing;
        }
    }

    private async void OnWindowClosing(object? sender, CancelEventArgs e)
    {
        if (DataContext is not MainViewModel mVM) return;

        e.Cancel = true; // Always cancel first; we will close manually if confirmed

        bool canClose = await mVM.ConfirmCloseAsync();
        if (canClose && _subscribedWindow != null)
        {
            _subscribedWindow.Closing -= OnWindowClosing;
            _subscribedWindow.Close();
        }
    }

    private void ToggleSplitPane(object? sender, PointerEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            if (vm.SettingsCtrl.GetIsManualPaneControl()) { return; }
            if (SplitViewPaneGrid.IsPointerOver)
            {
                MainSuiteSplitView.IsPaneOpen = true;
                MainSuiteSplitViewPaneScrollViewer.AllowAutoHide = false;
            }
            else
            {
                MainSuiteSplitView.IsPaneOpen = false;
                MainSuiteSplitViewPaneScrollViewer.AllowAutoHide = true;
            }
        }
    }

    private void MoveWindowOnPress(object? sender, PointerPressedEventArgs e)
    {
        ((Window)TopLevel.GetTopLevel(this)).BeginMoveDrag(e);
    }

    private void OnPointerPressed(object sender, PointerPressedEventArgs e)
    {
        if (DataContext is MainViewModel vm)
        {
            var pointerKind = e.GetCurrentPoint(this).Properties.PointerUpdateKind;
            switch (pointerKind)
            {
                case PointerUpdateKind.XButton1Pressed:
                    vm.GoBack();
                    break;
                case PointerUpdateKind.XButton2Pressed:
                    vm.GoForward();
                    break;
                default:
                    return;
            }
        }
    }

    private void TitleDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is Window window)
        {
            if (window.WindowState == WindowState.Maximized)
                window.WindowState = WindowState.Normal;
            else
                window.WindowState = WindowState.Maximized;
        }
    }

}