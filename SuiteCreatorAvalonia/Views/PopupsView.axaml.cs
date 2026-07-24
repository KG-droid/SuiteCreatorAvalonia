using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Styling;
using AvaloniaEdit.Highlighting;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.ViewModels;
using System;
using System.ComponentModel;

namespace SuiteCreatorAvalonia.Views;

public partial class PopupsView : UserControl
{
    private PropertyChangedEventHandler? _popupsPropChangeHandler;
    private PopupSampleWindow? _samplePopupWindow;
    private PopupSampleWindowViewModel? _samplePopupWindowViewModel;
    private PopupsViewModel? _popupsViewModel;
    private bool _unloadedSubscribed;

    public PopupsView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Load the custom syntax highlighting for the texteditor
        ThemeVariant themeVariant = App.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
        if (themeVariant == ThemeVariant.Light)
        {
            PSCondition_TextEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("PowerShellLight");
        }
        else
        {
            PSCondition_TextEditor.SyntaxHighlighting = HighlightingManager.Instance.GetDefinition("PowerShellDark");
        }
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (DataContext is PopupsViewModel pVM)
        {
            if (_popupsViewModel != null && _popupsViewModel == pVM) return;

            // Unsubscribe from the previous ViewModel
            UnsubscribeFromViewModel();

            _popupsViewModel = pVM;
            pVM.PropertyChanged += OnPopupsViewModelPropertyChanged;

            if (pVM.ShowPopupWarning && pVM.ShowPopupPreview)
            {
                OpenPopupSampleWindow();
            }
        }
    }

    private void UnsubscribeFromViewModel()
    {
        if (_popupsViewModel != null)
        {
            _popupsViewModel.PropertyChanged -= OnPopupsViewModelPropertyChanged;
            if (_popupsPropChangeHandler != null)
            {
                _popupsViewModel.PropertyChanged -= _popupsPropChangeHandler;
            }
        }
        UnsubscribeFromSampleWindowViewModel();
    }

    private void UnsubscribeFromSampleWindowViewModel()
    {
        if (_samplePopupWindowViewModel != null)
        {
            _samplePopupWindowViewModel.PropertyChanged -= OnSampleWindowViewModelPropertyChanged;
            _samplePopupWindowViewModel = null;
        }
    }

    private void OnPopupsViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (sender is not PopupsViewModel pVM) return;

        if (
            args.PropertyName == nameof(PopupsViewModel.ShowPopupWarning) ||
            args.PropertyName == nameof(PopupsViewModel.HasInstallPopup) ||
            args.PropertyName == nameof(PopupsViewModel.HasUninstallPopup)
        )
        {
            if (pVM.ShowPopupWarning && (pVM.HasInstallPopup || pVM.HasUninstallPopup))
            {
                if ((_samplePopupWindow == null || !_samplePopupWindow.IsLoaded) && pVM.ShowPopupPreview)
                {
                    OpenPopupSampleWindow();
                }
                else
                {
                    _samplePopupWindow?.Close();
                    OpenPopupSampleWindow();
                }
            }
            else
            {
                if (_samplePopupWindow != null && _samplePopupWindow.IsLoaded)
                {
                    _samplePopupWindow.Close();
                }
            }
        }
        else if (args.PropertyName == nameof(PopupsViewModel.ShowPopupPreview) && pVM.ShowPopupPreview)
        {
            OpenPopupSampleWindow();
        }
    }

    private void OnSampleWindowViewModelPropertyChanged(object? sender, PropertyChangedEventArgs args)
    {
        if (args.PropertyName == nameof(PopupSampleWindowViewModel.PopupForAction) && DataContext is PopupsViewModel pVM)
        {
            _samplePopupWindow?.Close();
            if (_popupsPropChangeHandler != null)
                pVM.PropertyChanged -= _popupsPropChangeHandler;
            pVM.ShowSampleFor = _samplePopupWindowViewModel!.PopupForAction;
            OpenPopupSampleWindow();
        }
    }

    public void OpenPopupSampleWindow()
    {
        if (Design.IsDesignMode) return; // Skip in design mode
        if (DataContext is PopupsViewModel pVM)
        {
            if (!pVM.HasInstallPopup && !pVM.HasUninstallPopup) return;

            // Clean up previous sample window VM subscription
            UnsubscribeFromSampleWindowViewModel();

            _samplePopupWindow = new();
            _samplePopupWindowViewModel = (PopupSampleWindowViewModel)pVM.ViewFact.GetVM(typeof(PopupSampleWindowViewModel));
            _samplePopupWindow.DataContext = _samplePopupWindowViewModel;
            if (pVM.ShowSampleFor == SuiteAction.Deployment)
            {
                _samplePopupWindowViewModel.MainText.Text = pVM.InstallTxt.Text;
                _samplePopupWindowViewModel.Action = pVM.InstAction;
                _samplePopupWindowViewModel.ActionIconKind = pVM.SelectedDeployIconKind;
            }
            else
            {
                _samplePopupWindowViewModel.MainText.Text = pVM.UninstallTxt.Text;
                _samplePopupWindowViewModel.Action = pVM.UninstAction;
                _samplePopupWindowViewModel.ActionIconKind = pVM.SelectedRemoveIconKind;
            }
            _samplePopupWindowViewModel.HasOtherPopupTypes = pVM.HasInstallPopup && pVM.HasUninstallPopup;
            _samplePopupWindowViewModel.IsClosuresAppsVisible = pVM.LinkToProcClosures;
            _samplePopupWindowViewModel.MaxDelayDays = pVM.DelayDays != null ? (int)pVM.DelayDays : 7;
            _samplePopupWindowViewModel.TimerExpireAction = pVM.TimerExpireAction;
            _samplePopupWindowViewModel.Timer = pVM.Timer != null ? (int)pVM.Timer : 40;
            _samplePopupWindowViewModel.PopupForAction = pVM.ShowSampleFor;
            _samplePopupWindowViewModel.Icon = pVM.SuiteLogo;
            _samplePopupWindow.Show();

            // Subscribe to Unloaded only once
            if (!_unloadedSubscribed)
            {
                Unloaded += OnPopupsViewUnloaded;
                _unloadedSubscribed = true;
            }

            _popupsPropChangeHandler = PopupsView_PropertyChanged(_samplePopupWindowViewModel, _samplePopupWindow);
            pVM.PropertyChanged += _popupsPropChangeHandler;
            _samplePopupWindowViewModel.PropertyChanged += OnSampleWindowViewModelPropertyChanged;
        }
    }

    private void OnPopupsViewUnloaded(object? sender, RoutedEventArgs e)
    {
        UnsubscribeFromViewModel();
        _samplePopupWindow?.Close();
    }

    public void SwitchPopupView_Click(object? sender, RoutedEventArgs e)
    {
        _samplePopupWindowViewModel.PopupForAction = SuiteAction.Deployment == _samplePopupWindowViewModel.PopupForAction
            ? SuiteAction.Removal
            : SuiteAction.Deployment;
    }

    private void ChangeSuiteLogoPopup_Closed(object? sender, EventArgs e)
    {
        if (DataContext is PopupsViewModel vm)
        {
            vm.SuiteLogoError = null;
        }
    }

    private PropertyChangedEventHandler PopupsView_PropertyChanged(PopupSampleWindowViewModel popupSampleWindowViewModel, PopupSampleWindow samplePopupWindow)
    {
        return (sender, args) =>
        {
            if (DataContext is not PopupsViewModel pVM)
                return;
            switch (args.PropertyName)
            {
                case nameof(PopupsViewModel.InstallTxt):
                    if (pVM.ShowSampleFor == SuiteAction.Deployment)
                        popupSampleWindowViewModel.MainText.Text = pVM.InstallTxt.Text;
                    else
                        popupSampleWindowViewModel.MainText.Text = pVM.UninstallTxt.Text;
                    break;
                case nameof(PopupsViewModel.ShowPopupPreview):
                    if (pVM.ShowPopupPreview)
                    {
                        if (!samplePopupWindow.IsLoaded)
                        {
                            OpenPopupSampleWindow();
                        }
                    }
                    else if (samplePopupWindow.IsLoaded)
                    {
                        pVM.PropertyChanged -= _popupsPropChangeHandler;
                        samplePopupWindow.Close();
                    }
                    break;
                case nameof(PopupsViewModel.InstAction):
                    if (pVM.ShowSampleFor == SuiteAction.Deployment)
                        popupSampleWindowViewModel.Action = pVM.InstAction;
                    break;
                case nameof(PopupsViewModel.UninstAction):
                    if (pVM.ShowSampleFor != SuiteAction.Deployment)
                        popupSampleWindowViewModel.Action = pVM.UninstAction;
                    break;
                case nameof(PopupsViewModel.SelectedDeployIconKind):
                    if (pVM.ShowSampleFor == SuiteAction.Deployment)
                        popupSampleWindowViewModel.ActionIconKind = pVM.SelectedDeployIconKind;
                    break;
                case nameof(PopupsViewModel.SelectedRemoveIconKind):
                    if (pVM.ShowSampleFor != SuiteAction.Deployment)
                        popupSampleWindowViewModel.ActionIconKind = pVM.SelectedRemoveIconKind;
                    break;
                case nameof(PopupsViewModel.TimerExpireAction):
                    popupSampleWindowViewModel.TimerExpireAction = pVM.TimerExpireAction;
                    break;
                case nameof(PopupsViewModel.LinkToProcClosures):
                    popupSampleWindowViewModel.IsClosuresAppsVisible = pVM.LinkToProcClosures;
                    break;
                case nameof(PopupsViewModel.Timer):
                    popupSampleWindowViewModel.Timer = pVM.Timer != null ? (int)pVM.Timer : 40;
                    break;
                case nameof(PopupsViewModel.DelayDays):
                    popupSampleWindowViewModel.MaxDelayDays = pVM.DelayDays != null ? (int)pVM.DelayDays : 7;
                    break;
                case nameof(PopupsViewModel.SuiteLogo):
                    popupSampleWindowViewModel.Icon = pVM.SuiteLogo;
                    break;
            }
        };
    }
}