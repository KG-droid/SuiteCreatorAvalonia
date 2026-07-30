using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons.Avalonia;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.IDataTemplates;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    public partial class MainViewModel : ViewModelBase
    {
        private readonly Stack<Type> _backNavigationStack = new Stack<Type>();
        private readonly Stack<Type> _forwardNavigationStack = new Stack<Type>();
        private readonly SuiteCoreManager _suiteCoreManager;
        private readonly SuiteBuilder _suiteBuilder;
        private readonly ViewFactory _tabFac;

        [ObservableProperty]
        private bool _isPaneOpen = false;

        [ObservableProperty]
        private bool _isAllowPaneScrollAutoHide = true;

        [ObservableProperty]
        private ViewModelBase _currentTabView;

        [ObservableProperty]
        private string _suiteTitle;

        public bool HasUnsavedChanges => _suiteCoreManager.HasUnsavedChanges;

        [ObservableProperty]
        private List<SuiteValidationError>? _buildValidationErrors = new();

        [ObservableProperty]
        private ItemsControl _buildValidationErrorsItemsControl;

        [ObservableProperty]
        private DateTime? _buildCompleteDateTime;

        public string? BuildCompleteDateTimeStr
        {
            get => BuildCompleteDateTime?.ToString(CultureInfo.CurrentCulture);
        }

        [ObservableProperty]
        public string? _buildPath;

        [ObservableProperty]
        public string? _detectionRule;

        partial void OnBuildCompleteDateTimeChanged(DateTime? oldValue, DateTime? newValue)
        {
            if (oldValue != newValue)
            {
                OnPropertyChanged(nameof(BuildCompleteDateTimeStr));
                SetBuildPanelState(false, true);
            }
        }

        partial void OnBuildValidationErrorsChanged(List<SuiteValidationError>? value)
        {
            // ItemsSource must be nulled out first - reassigning a non-observable List directly
            // leaves the previously realized item containers in place instead of replacing them.
            BuildValidationErrorsItemsControl.ItemsSource = null;
            if (value != null && value.Count > 0)
            {
                BuildValidationErrorsItemsControl.ItemsSource = value;
                SetBuildPanelState(true, false);
            }
        }

        [ObservableProperty]
        private bool _buildErrorsPanelVisible = false;

        [ObservableProperty]
        private bool _buildSuccessPanelVisible = false;

        [ObservableProperty]
        private bool _buildSplitterVisible = false;

        private void SetBuildPanelState(bool showErrors, bool showSuccess)
        {
            BuildErrorsPanelVisible = showErrors;
            BuildSuccessPanelVisible = showSuccess;
            BuildSplitterVisible = showErrors || showSuccess;
        }

        [RelayCommand]
        public void HideBuildErrors()
        {
            SetBuildPanelState(false, false);
        }

        [RelayCommand]
        public void HideBuildSuccess()
        {
            SetBuildPanelState(false, false);
        }

        public void ResetBuildPanel()
        {
            SetBuildPanelState(false, false);
        }

        public AppSettingsControl SettingsCtrl; // Accessed via the View code

        public ObservableCollection<ToggleButton> ToggleButtons { get; set; } = new ObservableCollection<ToggleButton>();

        public ObservableCollection<ToggleButton> PinnedToggleButtons { get; set; } = new ObservableCollection<ToggleButton>();

        partial void OnCurrentTabViewChanged(ViewModelBase value)
        {
            IEnumerable<ToggleButton> combinedItems = ToggleButtons.Concat(PinnedToggleButtons);
            foreach (ToggleButton btn in combinedItems)
            {
                if (btn.Tag is Type btnType)
                    if (btnType != CurrentTabView.GetType())
                    {
                        btn.IsEnabled = true;
                        btn.IsChecked = false;
                    }
                    else
                    {
                        btn.IsEnabled = false;
                        if ((bool)!btn.IsChecked) { btn.IsChecked = true; }
                    }
            }
        }

        public MainViewModel() : this(new ViewFactory(
            type =>
            {
                return (ViewModelBase)Activator.CreateInstance(type)!;
            }),
            new AppSettingsControl(), new SuiteCoreManager(), new SuiteBuilder(new SuiteCoreManager(), new AppSettingsControl()))
        {
        }

        public MainViewModel(ViewFactory tabFac, AppSettingsControl settingsCtrl, SuiteCoreManager suiteCoreManager, SuiteBuilder suiteBuilder)
        {
            _suiteCoreManager = suiteCoreManager;
            _suiteBuilder = suiteBuilder;
            _suiteTitle = _suiteCoreManager.GetProjectName();
            _suiteCoreManager.ProjectNameChanged += (s, e) =>
            {
                SuiteTitle = _suiteCoreManager.GetProjectName();
            };
            _suiteCoreManager.ProjectCleared += OnProjectCleared;
            _suiteCoreManager.ProjectLoaded += OnProjectLoaded;
            _suiteCoreManager.ProjectChanged += OnProjectChanged;
            _suiteCoreManager.LoadFailed += async (s, msg) => await ShowErrorPop("Failed to Load Project", msg);
            _suiteCoreManager.HasUnsavedChangesChanged += (s, hasChanges) =>
            {
                OnPropertyChanged(nameof(HasUnsavedChanges));
                UpdateSuiteTitle();
            };
            _suiteBuilder.SuiteBuilt += OnBuildChanged;
            _tabFac = tabFac;
            SettingsCtrl = settingsCtrl;
            HelpService.Instance.NavigateToPage = type => SetView(type);
            CurrentTabView = tabFac.GetVM(typeof(PackageViewModel));
            _backNavigationStack.Push(CurrentTabView.GetType());
            ConfigureTabButtons();
            BuildValidationErrorsItemsControl = new ItemsControl();
            BuildValidationErrorsItemsControl.DataTemplates.Add(new BuildValidationErrorTemplate(this, _suiteCoreManager));
        }

        private void OnProjectChanged(object? sender, object? changedObject)
        {
            UpdateSuiteTitle();
            if (BuildValidationErrors != null && BuildValidationErrors.Count > 0 && changedObject != null)
            {
                foreach (var err in BuildValidationErrors)
                {
                    if (err.ObjectForError.GetType() == changedObject.GetType())
                    {
                        List<SuiteValidationError>? latestErrors = _suiteBuilder.ValidateConfig();
                        if (latestErrors != null && latestErrors.Count > 0)
                        {
                            BuildValidationErrors = latestErrors;
                        }
                        else
                        {
                            BuildValidationErrors = null;
                        }
                        break;
                    }
                }
            }
        }

        private void OnProjectCleared(object? sender, EventArgs e)
        {
            UpdateSuiteTitle();
            CurrentTabView = _tabFac.GetVM(typeof(PackageViewModel));
            BuildValidationErrors = new();
        }

        private void OnProjectLoaded(object? sender, EventArgs e)
        {
            UpdateSuiteTitle();
            CurrentTabView = _tabFac.GetVM(typeof(PackageViewModel));
            BuildValidationErrors = new();
        }

        private void OnBuildChanged(object? sender, SuiteBuiltEventArgs e)
        {
            BuildCompleteDateTime = e.BuildTime;
            BuildPath = e.BuildPath;
            DetectionRule = _suiteCoreManager.GetBuildSettings().Detection;
        }

        private void ConfigureTabButtons()
        {
            // Use EventTabMappings instead of ViewFactory.Tab
            var eventTabs = _tabFac.EventTabs;
            var tabProperties = eventTabs.GetType().GetProperties();
            bool first = true;
            foreach (var property in tabProperties)
            {
                if (property.PropertyType.BaseType == typeof(EventTabMappings.Tab))
                {
                    var propertyValue = (EventTabMappings.Tab)property.GetValue(eventTabs);
                    ToggleButton toggleButton = new ToggleButton();
                    toggleButton.Height = 40;
                    toggleButton.Padding = new Avalonia.Thickness(0);
                    toggleButton.Background = Brushes.Transparent;
                    toggleButton.Margin = new Avalonia.Thickness(0, 0, 0, 1);
                    toggleButton.Tag = propertyValue.ContentType;
                    toggleButton.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                    if (first)
                    {
                        toggleButton.IsChecked = true;
                        toggleButton.IsEnabled = false;
                        first = false;
                    }
                    toggleButton.Command = new RelayCommand<Type>((tag) =>
                    {
                        if (tag != null)
                            SetView(tag);
                    });
                    toggleButton.CommandParameter = toggleButton.Tag;
                    Style checkedStyle = new Style(x => x.OfType<ToggleButton>().Class(":checked").Template().OfType<ContentPresenter>());
                    checkedStyle.Setters.Add(new Setter(ToggleButton.BackgroundProperty, Brushes.Transparent));
                    Style pressedStyle = new Style(x => x.OfType<ToggleButton>().Class(":pressed").Template().OfType<ContentPresenter>());
                    pressedStyle.Setters.Add(new Setter(ToggleButton.BackgroundProperty, Brushes.Transparent));
                    Style disabledStyle = new Style(x => x.OfType<ToggleButton>().Class(":disabled").Template().OfType<ContentPresenter>());
                    disabledStyle.Setters.Add(new Setter(ToggleButton.BackgroundProperty, Brushes.Transparent));
                    toggleButton.Styles.Add(checkedStyle);
                    toggleButton.Styles.Add(pressedStyle);
                    toggleButton.Styles.Add(disabledStyle);
                    Grid grid = new Grid();
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Parse("10")));
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    grid.Margin = new Avalonia.Thickness(3, 0, 0, 0);
                    grid.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left;
                    Border border = new Border();
                    border.CornerRadius = new Avalonia.CornerRadius(5);
                    border.Background = Brushes.Transparent;
                    Grid.SetColumn(border, 0);
                    Grid.SetColumnSpan(border, 2);
                    Border newBorder = new Border();
                    newBorder.Bind(
                        TextBlock.IsVisibleProperty,
                        new Binding("IsChecked")
                        { 
                            Mode = BindingMode.OneWay,
                            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(ToggleButton) } 
                        }
                    );
                    newBorder.CornerRadius = new Avalonia.CornerRadius(5);
                    newBorder.Height = 18;
                    newBorder.Width = 5;
                    newBorder.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                    newBorder.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                    newBorder.Background = SolidColorBrush.Parse("#04ab01");
                    Grid.SetColumn(newBorder, 0);
                    MaterialIcon materialIcon = new MaterialIcon { Kind = propertyValue.IconKind };
                    materialIcon.Foreground = propertyValue.IconColor;
                    materialIcon.Height = 25;
                    materialIcon.Width = 25;
                    Grid.SetColumn(materialIcon, 1);
                    TextBlock textBlock2 = new TextBlock();
                    textBlock2.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center;
                    textBlock2.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                    textBlock2.Text = propertyValue.Header;
                    textBlock2.Classes.Add("FieldName");
                    textBlock2.Foreground = Brushes.Gray;
                    textBlock2.Margin = new Avalonia.Thickness(8, 0, 0, 0);
                    textBlock2.Bind(
                        TextBlock.IsVisibleProperty,
                        new Binding("IsPaneOpen")
                        {
                            Mode = BindingMode.OneWay,
                            RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(SplitView) }
                        }
                    );
                    Grid.SetColumn(textBlock2, 2);
                    grid.Children.Add(border);
                    grid.Children.Add(newBorder);
                    grid.Children.Add(materialIcon);
                    grid.Children.Add(textBlock2);
                    toggleButton.Content = grid;
                    if (propertyValue.Pinned)
                        PinnedToggleButtons.Add(toggleButton);
                    else
                        ToggleButtons.Add(toggleButton);
                }
            }
        }

        public void GoBack()
        {
            if (_backNavigationStack.Count > 0)
            {
                _forwardNavigationStack.Push(CurrentTabView.GetType());
                CurrentTabView = _tabFac.GetVM(_backNavigationStack.Pop());
            }
        }

        public void GoForward()
        {
            if (_forwardNavigationStack.Count > 0)
            {
                _backNavigationStack.Push(CurrentTabView.GetType());
                CurrentTabView = _tabFac.GetVM(_forwardNavigationStack.Pop());
            }
        }

        public void SetView(Type viewModelType)
        {
            if (viewModelType is Type tagType)
            {
                _backNavigationStack.Push(CurrentTabView.GetType());
                CurrentTabView = _tabFac.GetVM(tagType);
            }
        }

        [RelayCommand]
        public void TogglePaneManualControl()
        {
            if (!(bool)SettingsCtrl.GetIsManualPaneControl())
                SettingsCtrl.UpdateIsManualPaneControl(true);
            if (IsPaneOpen)
            {
                IsPaneOpen = false;
                IsAllowPaneScrollAutoHide = true;
            }
            else
            {
                IsPaneOpen = true;
                IsAllowPaneScrollAutoHide = false;
            }
        }

        [RelayCommand]
        public async Task SaveProject()
        {
            string? savePath = _suiteCoreManager.GetSavePath();
            try
            {
                if (string.IsNullOrEmpty(savePath))
                {
                    await SaveAsProject();
                }
                else
                {
                    await _suiteCoreManager.SaveAsync();
                }
            }
            catch (Exception ex)
            {
                await ShowErrorPop($"Failed to save to file: {savePath}", ex.Message);
            }
        }

        [RelayCommand]
        public async Task SaveAsProject()
        {
            string? savePath = await this.SaveFileDialogAsync(new FilePickerSaveOptions() { Title = "Project Save", FileTypeChoices = SysIOPickerTypes.SuiteProject });
            if (!string.IsNullOrWhiteSpace(savePath))
            {
                try
                {
                    ResetBuildPanel();
                    await _suiteCoreManager.SaveAsync(savePath);
                    UpdateSuiteTitle();
                }
                catch (Exception ex)
                {
                    await ShowErrorPop($"Failed to save to file: {savePath}", ex.Message);
                }
            }
        }

        [RelayCommand]
        public async Task OpenProject()
        {
            IEnumerable<string>? results = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Select a Project file", FileTypeFilter = SysIOPickerTypes.SuiteProject });
            if (results != null && results.Count() > 0)
            {
                try
                {
                    ResetBuildPanel();
                    await _suiteCoreManager.LoadAsync(results.First());
                }
                catch (Exception ex)
                {
                    await ShowErrorPop($"Failed to open {Path.GetFileName(results.First())}", ex.Message);
                }
            }
        }

        [RelayCommand]
        public async Task ImportProject()
        {
            ImportProjectViewModel importVm = new ImportProjectViewModel();
            object? importResult = await this.ShowDialogAsync(importVm);
            if (importResult is not string strImport || strImport != "Import")
                return;

            string importFilePath = importVm.SuiteExePath!;
            string outputFolder = importVm.OutputFolder!;

            if (Directory.Exists(outputFolder) && Directory.EnumerateFileSystemEntries(outputFolder).Any())
            {
                var dialogVm = new DialogHostViewModel(
                    "Folder Not Empty",
                    $"The selected folder is not empty:\n{outputFolder}\n\nExisting files may be overwritten. Do you want to continue?",
                    DialogHostViewModel.MsgTypes.Warning,
                    DialogHostViewModel.PopupTypes.Question,
                    "Continue",
                    "Cancel",
                    false,
                    null,
                    null
                );

                object? dialogResult = await this.ShowDialogAsync(dialogVm);
                if (dialogResult is not string strResult || strResult != "Continue")
                    return;
            }

            var progressDialogVM = new ProgressDiagViewModel
            {
                Message = "Starting Import",
            };
            _ = this.ShowDialogAsync(progressDialogVM);
            try
            {
                ResetBuildPanel();
                var progress = new Progress<string>(stage => progressDialogVM.Message = stage);
                await _suiteCoreManager.ImportSuiteAsync(importFilePath, outputFolder, progress);
            }
            catch (Exception ex)
            {
                AppLog.Error($"Failed to import suite from: {importFilePath}", ex, "SuiteCore");
                await ShowErrorPop($"Failed to import suite {Path.GetFileName(importFilePath)}", ex.Message);
            }
            finally
            {
                this.CloseDialog();
            }
        }

        [RelayCommand]
        public async Task NewProject()
        {
            // If there are unsaved changes, confirm with the user before clearing
            if (HasUnsavedChanges)
            {
                var dialogVm = new DialogHostViewModel(
                    "Unsaved Changes",
                    "You have unsaved changes. Do you want to discard them and start a new project?",
                    DialogHostViewModel.MsgTypes.Warning,
                    DialogHostViewModel.PopupTypes.Question,
                    "Discard",
                    "Keep",
                    false,
                    null,
                    null
                );

                var result = await this.ShowDialogAsync(dialogVm);

                if (result is string strResult)
                {
                    if (strResult != "Discard")
                    {
                        // User chose to keep changes or cancelled
                        return;
                    }
                }
                else
                {
                    // If no explicit discard result, do not proceed
                    return;
                }
            }

            _suiteCoreManager.SetDefaultConfig();
            ResetBuildPanel();
        }

        private void UpdateSuiteTitle()
        {
            string baseName = _suiteCoreManager.GetProjectName();
            SuiteTitle = _suiteCoreManager.HasUnsavedChanges ? baseName + "*" : baseName;
        }

        [RelayCommand]
        public async Task ShowAbout()
        {
            await this.ShowDialogAsync(new AboutViewModel());
        }

        [RelayCommand]
        public async Task HelpOnPage()
        {
            await HelpService.Instance.StartPageHelpAsync();
        }

        [RelayCommand]
        public void WhatsThis()
        {
            HelpService.Instance.StartInspect();
        }

        [RelayCommand]
        public async Task GettingStartedTour()
        {
            await HelpService.Instance.StartTourAsync(HelpTours.GettingStarted());
        }

        [RelayCommand]
        public void ClearValidationErrors()
        {
            BuildValidationErrors = new();
        }

        [RelayCommand]
        public void OpenBuildFolder()
        {
            if (!string.IsNullOrWhiteSpace(BuildPath) && Path.Exists(BuildPath))
            { 
                Process.Start("explorer.exe", $"\"{BuildPath}\"");
            }
        }

        /// <summary>
        /// Set when the app was launched with a project file path argument (e.g. double-clicking a .scp file).
        /// Checked by <see cref="CheckAutoRecoveryAsync"/> before any autosave recovery prompt.
        /// </summary>
        public string? PendingOpenFilePath { get; set; }

        /// <summary>
        /// Called on startup. If launched with a project file argument, opens it directly.
        /// Otherwise, if any autosaves exist, prompts the user to choose one to recover or skip all.
        /// </summary>
        public async Task CheckAutoRecoveryAsync()
        {
            if (!string.IsNullOrWhiteSpace(PendingOpenFilePath) && File.Exists(PendingOpenFilePath))
            {
                try
                {
                    ResetBuildPanel();
                    await _suiteCoreManager.LoadAsync(PendingOpenFilePath);
                }
                catch (Exception ex)
                {
                    await ShowErrorPop($"Failed to open {Path.GetFileName(PendingOpenFilePath)}", ex.Message);
                }
                return;
            }

            IReadOnlyList<string> autoSaves = SuiteCoreManager.GetAllAutoSavePaths();
            if (autoSaves.Count == 0) return;

            AutoRecoveryViewModel recoveryVm = new AutoRecoveryViewModel(autoSaves);
            object? result = await this.ShowDialogAsync(recoveryVm);

            if (result is AutoRecoveryEntryViewModel chosen)
            {
                try
                {
                    await _suiteCoreManager.LoadAutoSaveAsync(chosen.FilePath);
                    SuiteTitle = _suiteCoreManager.GetProjectName() + " (Recovered)*";
                    // Delete only the chosen file; others stay for next launch
                    SuiteCoreManager.DeleteAutoSaveAt(chosen.FilePath);
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Failed to restore auto-recovered save from: {chosen.FilePath}", ex, "SuiteCore");
                    await ShowErrorPop("Failed to restore auto-recovered save", ex.Message);
                    SuiteCoreManager.DeleteAutoSaveAt(chosen.FilePath);
                }
            }
            else if (result is AutoRecoveryRemindResult)
            {
                // User chose "Remind Me Next Time" — leave all autosave files untouched.
            }
            else
            {
                // User chose "Discard All" (null result) — delete all autosave files.
                foreach (string path in autoSaves)
                    SuiteCoreManager.DeleteAutoSaveAt(path);
            }
        }

        /// <summary>
        /// Returns true if the window should be allowed to close, false if the close should be cancelled.
        /// On a clean close, prompts about unsaved changes and then deletes the autosave.
        /// </summary>
        public async Task<bool> ConfirmCloseAsync()
        {
            if (HasUnsavedChanges)
            {
                var dialogVm = new DialogHostViewModel(
                    "Unsaved Changes",
                    "You have unsaved changes. Do you want to save before closing?",
                    DialogHostViewModel.MsgTypes.Warning,
                    DialogHostViewModel.PopupTypes.Question,
                    "Save",
                    "Discard & Close",
                    true,
                    null,
                    null
                );

                object? result = await this.ShowDialogAsync(dialogVm);

                if (result is string strResult)
                {
                    if (strResult == "Save")
                    {
                        await SaveProject();
                        if (HasUnsavedChanges)
                            return false; // Save was cancelled (e.g. no path chosen)
                    }
                    else if (strResult == "Discard & Close")
                    {
                        // Fall through to allow close
                    }
                    else
                    {
                        return false; // Cancelled
                    }
                }
                else
                {
                    return false; // Dialog closed without selection (Cancel)
                }
            }

            _suiteCoreManager.DeleteAutoSave();
            return true;
        }
    }
}
