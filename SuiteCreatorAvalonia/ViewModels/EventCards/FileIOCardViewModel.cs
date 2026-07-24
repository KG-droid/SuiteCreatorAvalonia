using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Material.Icons.Avalonia;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels.EventCards
{
    internal partial class FileIOCardViewModel : EventCardWithPermanenceViewModelBase
    {
        private SuiteCoreManager _coreManager;
        private bool _isLoading = false;
        private PathVarsTextBoxViewModel _sourcePathVM = new() { DontShowUserVars = true, IsAddFileButtonVisible = false, PlaceholderText = "Enter a Source Path, variables can be used via the '{' key" };
        private PathVarsTextBoxViewModel _destPathVM = new() { IsAddFileButtonVisible = false, PlaceholderText = "Enter a Destination Path, variables can be used via the '{' key" };

        [ObservableProperty]
        private FileSysIOType _iOType = FileSysIOType.File;

        [ObservableProperty]
        private FileSysIOAction _action = FileSysIOAction.Deploy;

        [ObservableProperty]
        private bool _overridePermissions = false;

        [ObservableProperty]
        private bool _replaceExisting = true;

        [ObservableProperty]
        private Brush _lockColour = new SolidColorBrush(Colors.White);

        private Flyout _permissionsFlyout;

        [ObservableProperty]
        private ObservableCollection<Permission> _permissions = new();

        private void PermissionsChanged(object? s, AvaloniaPropertyChangedEventArgs e)
        {
            if (Permissions != null && Permissions.Any())
            {
                OverridePermissions = true;
            }
            else
            {
                OverridePermissions = false;
            }
        }

        public FileIOCardViewModel() : this(
            new FileSysIO(),
            new SuiteCoreManager())
        {
        }

        public FileIOCardViewModel(FileSysIO fileEvent, SuiteCoreManager suiteCoreManager) : base(fileEvent, suiteCoreManager)
        {
            _coreManager = suiteCoreManager;
            FileSysPermissionsViewModel vm = new FileSysPermissionsViewModel();
            _permissionsFlyout = new Flyout() { Content = vm };
            vm.PermissionsList = Permissions;
            _permissionsFlyout.PropertyChanged += (sender, e) => OnPropertyChanged(nameof(Permissions));
            _permissionsFlyout.PropertyChanged += PermissionsChanged;
            if (fileEvent != null)
                LoadEvent(fileEvent);
            CreateInnerCardView();
        }

        partial void OnIOTypeChanged(FileSysIOType value)
        {
            CreateInnerCardView();
        }

        partial void OnActionChanged(FileSysIOAction value)
        {
            CreateInnerCardView();
        }

        partial void OnOverridePermissionsChanged(bool value)
        {
            if (value)
            {
                LockColour = new SolidColorBrush(Colors.Red);
            }
            else
            {
                LockColour = new SolidColorBrush(Colors.White);
            }
        }

        private void CreateInnerCardView()
        {
            string browseDiagTitle;
            string txtWatermark;
            MaterialIconKind iconKind;
            switch (IOType)
            {
                case FileSysIOType.File:
                default:
                    browseDiagTitle = "Browse for a file";
                    txtWatermark = "Enter or browse for a File path";
                    iconKind = MaterialIconKind.File;
                    break;
                case FileSysIOType.Directory:
                    browseDiagTitle = "Browse for a Directory";
                    txtWatermark = "Enter or browse for a Directory path";
                    iconKind = MaterialIconKind.Folder;
                    break;
            }

            Grid mainGrid = new Grid();
            mainGrid.Children.Clear();

            // Type
            ComboBox typeCombo = new ComboBox();
            typeCombo.ItemsSource = Enum.GetValues(typeof(FileSysIOType));
            typeCombo.Bind(ComboBox.SelectedValueProperty, new Binding("IOType"));
            typeCombo.Background = Brushes.Transparent;
            typeCombo.Margin = new Avalonia.Thickness(0, 0, 5, 0);
            typeCombo.SelectionChanged += HoldCardOpenAfterComboChange;
            typeCombo.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Help.Annotate(typeCombo, "Type", "Whether this event acts on a single File or a whole Directory.");
            Grid.SetColumn(typeCombo, 0);
            mainGrid.Children.Add(typeCombo);

            // Action
            ComboBox actionCombo = new ComboBox();
            actionCombo.ItemsSource = Enum.GetValues(typeof(FileSysIOAction));
            actionCombo.Bind(ComboBox.SelectedValueProperty, new Binding("Action"));
            actionCombo.Background = Brushes.Transparent;
            actionCombo.SelectionChanged += HoldCardOpenAfterComboChange;
            actionCombo.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Help.Annotate(actionCombo, "Action", "Deploy a new file/folder, Copy or Move an existing one, Delete one, or Rename one.");
            Grid.SetColumn(actionCombo, 1);
            mainGrid.Children.Add(actionCombo);

            // Source
            if (Action == FileSysIOAction.Delete || Action == FileSysIOAction.Rename)
            {
                _sourcePathVM.DontShowUserVars = false;
            }
            else
            {
                // Disabling User vars as a source path for copy/move files from.
                // Putting files into the users profile is a reasonable use case, but taking files from it I dont think we should allow.
                _sourcePathVM.DontShowUserVars = true;
            }
            _sourcePathVM.CMDUpdated += (s, e) =>
            {
                SaveEvent();
            };
            PathVarsTextBoxView sourcePathVarsTextBoxView = new();
            sourcePathVarsTextBoxView.DataContext = _sourcePathVM;
            Help.Annotate(sourcePathVarsTextBoxView, "Source path", "The file or folder this event acts on. Type '{' to insert a suite variable, or use the browse button.");
            Grid.SetColumn(sourcePathVarsTextBoxView, 2);
            mainGrid.Children.Add(sourcePathVarsTextBoxView);

            Button sourceBrowseBtn = new Button();
            sourceBrowseBtn.Classes.Add("IconButton");
            sourceBrowseBtn.Tag = iconKind.ToString();
            sourceBrowseBtn.Command = new RelayCommand(() => SourceBrowse(browseDiagTitle, IOType));
            sourceBrowseBtn.Margin = new Avalonia.Thickness(5, 0, 5, 0);
            ToolTip.SetTip(sourceBrowseBtn, browseDiagTitle);
            Help.Annotate(sourceBrowseBtn, "Browse", browseDiagTitle + ".");
            Grid.SetColumn(sourceBrowseBtn, 3);
            mainGrid.Children.Add(sourceBrowseBtn);

            // Rest of the card
            switch (Action)
            {
                case FileSysIOAction.Deploy:
                case FileSysIOAction.Copy:
                case FileSysIOAction.Move:
                    // Deploy, Copy, Move card
                    mainGrid.ColumnDefinitions = new ColumnDefinitions("Auto, Auto, *, Auto, Auto, *, Auto, Auto, Auto, Auto");

                    // Pointer
                    MaterialIcon arrow = new();
                    arrow.Kind = MaterialIconKind.ArrowRightBoldOutline;
                    arrow.Margin = new Avalonia.Thickness(5, 0, 5, 0);
                    arrow.Height = 20;
                    arrow.Width = 20;
                    arrow.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                    ToolTip.SetTip(arrow, "Destination");
                    Grid.SetColumn(arrow, 4);
                    mainGrid.Children.Add(arrow);

                    // Destination Path
                    _destPathVM.CMDUpdated += (s, e) =>
                    {
                        SaveEvent();
                    };
                    PathVarsTextBoxView destinationPathVarsTextBoxViewModel = new();
                    destinationPathVarsTextBoxViewModel.DataContext = _destPathVM;
                    Help.Annotate(destinationPathVarsTextBoxViewModel, "Destination path", "Where the file or folder is copied/moved to. Type '{' to insert a suite variable, or use the browse button.");
                    Grid.SetColumn(destinationPathVarsTextBoxViewModel, 5);
                    mainGrid.Children.Add(destinationPathVarsTextBoxViewModel);

                    Button destinationBrowseButton = new();
                    destinationBrowseButton.Classes.Add("IconButton");
                    destinationBrowseButton.Tag = "FolderOpen";
                    destinationBrowseButton.Margin = new Avalonia.Thickness(5, 0, 5, 0);
                    destinationBrowseButton.Command = new RelayCommand(DestinationBrowse);
                    ToolTip.SetTip(destinationBrowseButton, browseDiagTitle);
                    Help.Annotate(destinationBrowseButton, "Browse", "Browse for a destination directory.");
                    Grid.SetColumn(destinationBrowseButton, 6);
                    mainGrid.Children.Add(destinationBrowseButton);

                    // Toggle Replace Existing
                    ToggleButton replaceExistingToggle = new();
                    replaceExistingToggle.Classes.Add("IconToggleButton");
                    replaceExistingToggle.Bind(ToggleButton.IsCheckedProperty, new Binding("ReplaceExisting"));
                    replaceExistingToggle.Tag = "FileReplace";
                    ToolTip.SetTip(replaceExistingToggle, "Toggle Replace existing");
                    Help.Annotate(replaceExistingToggle, "Replace existing", "Toggle whether an existing file/folder at the destination is overwritten.");
                    Grid.SetColumn(replaceExistingToggle, 7);
                    mainGrid.Children.Add(replaceExistingToggle);

                    // Toggle Permanent
                    ToggleButton permanentFileToggle = new();
                    permanentFileToggle.Classes.Add("IconToggleButton");
                    permanentFileToggle.Bind(ToggleButton.IsCheckedProperty, new Binding("IsPermanent"));
                    permanentFileToggle.Tag = "DiamondStone";
                    ToolTip.SetTip(permanentFileToggle, "If enabled, the file won't be removed on Suite removal");
                    Help.Annotate(permanentFileToggle, "Permanent", "If enabled, this file/folder won't be removed on Suite removal.");
                    Grid.SetColumn(permanentFileToggle, 8);
                    mainGrid.Children.Add(permanentFileToggle);

                    // Set permissions
                    Button overridePermissionsButton = new();
                    overridePermissionsButton.Classes.Add("IconButton");
                    overridePermissionsButton.Bind(Button.ForegroundProperty, new Binding("LockColour"));
                    overridePermissionsButton.Flyout = _permissionsFlyout;
                    overridePermissionsButton.Tag = "Lock";
                    ToolTip.SetTip(overridePermissionsButton, "Open Permissions");
                    Help.Annotate(overridePermissionsButton, "Permissions", "Set custom NTFS permissions on the deployed file/folder, overriding the defaults it would otherwise inherit.");
                    Grid.SetColumn(overridePermissionsButton, 9);
                    mainGrid.Children.Add(overridePermissionsButton);
                    break;
                case FileSysIOAction.Delete:
                    // Delete card
                    mainGrid.ColumnDefinitions = new ColumnDefinitions("Auto, Auto, *, Auto");
                    break;
                case FileSysIOAction.Rename:
                    // Rename card
                    mainGrid.ColumnDefinitions = new ColumnDefinitions("Auto, Auto, *, Auto, Auto, *");

                    // Pointer
                    TextBlock arrowBlock2 = new TextBlock();
                    arrowBlock2.Text = "=>";
                    arrowBlock2.Margin = new Avalonia.Thickness(7, 0, 5, 0);
                    arrowBlock2.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
                    Grid.SetColumn(arrowBlock2, 4);
                    mainGrid.Children.Add(arrowBlock2);

                    // New Name
                    TextBox newNameTextBlock = new TextBox();
                    newNameTextBlock.PlaceholderText = "New Name"; ;
                    Help.Annotate(newNameTextBlock, "New name", "The new name to give the file or folder.");
                    newNameTextBlock.Bind(TextBox.TextProperty, new Binding("DestPath"));
                    newNameTextBlock.Margin = new Thickness(0, 0, 10, 0);
                    Grid.SetColumn(newNameTextBlock, 5);
                    mainGrid.Children.Add(newNameTextBlock);
                    break;
            }
            CardInnerView = mainGrid;
        }

        private async void SourceBrowse(string title, FileSysIOType ioType)
        {
            IEnumerable<string> result;
            switch (ioType)
            {
                case FileSysIOType.File:
                default:
                    result = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = title, FileTypeFilter = SysIOPickerTypes.Any });
                    break;
                case FileSysIOType.Directory:
                    result = await this.OpenFolderDialogAsync(new FolderPickerOpenOptions() { AllowMultiple = false, Title = title });
                    break;
            }
            if (null != result && result.Any())
            {
                _sourcePathVM.VariablePath.Clear();
                _sourcePathVM.VariablePath.Add(new LiteralText(result.First()));
            }
        }

        private async void DestinationBrowse()
        {
            var result = await this.OpenFolderDialogAsync(new FolderPickerOpenOptions() { AllowMultiple = false, Title = "Browse for a directory" });
            if (null != result && result.Any())
            {
                _destPathVM.VariablePath.Clear();
                _destPathVM.VariablePath.Add(new LiteralText(result.First()));
            }
        }

        public override void LoadEvent(EventCore eventCore)
        {
            if (eventCore is FileSysIO fileIO)
            {
                _isLoading = true;
                IOType = fileIO.FileSysIOType;
                Action = fileIO.Action;
                _sourcePathVM.VariablePath.Clear();
                if (fileIO.SourcePath != null)
                {
                    _sourcePathVM.VariablePath.AddRange(fileIO.SourcePath);
                }
                _destPathVM.VariablePath.Clear();
                if (fileIO.DestinationPath != null)
                {
                    _destPathVM.VariablePath.AddRange(fileIO.DestinationPath);
                }
                OverridePermissions = fileIO.OverridePermissions;
                ReplaceExisting = fileIO.ReplaceExisting;
                IsPermanent = fileIO.IsPermanent;
                Permissions.Clear();
                if (fileIO.Permissions != null)
                {
                    Permissions.AddRange(fileIO.Permissions);
                }
                Schedules.Clear();
                Schedules.AddRange(fileIO.Schedules);
                // Ensure that the EventStage and Condition in each Schedule is the same instance as in SuiteStages/SuiteConditions for the ComboBox binding to work correctly.
                foreach (Schedule sch in Schedules)
                {
                    if (sch.EventStage != null)
                    {
                        sch.EventStage = SuiteStages.First(s => s.Id == sch.EventStage.Id);
                    }
                    if (sch.Condition != null)
                    {
                        sch.Condition = SuiteRules.First(s => s.Id == sch.Condition.Id);
                    }
                }
                LinkedEvent = fileIO;
                _isLoading = false;
            }
        }

        public override void SaveEvent()
        {
            if (_isLoading) return;
            if (LinkedEvent is FileSysIO file)
            {
                file.Permissions = Permissions.ToList();
                file.OverridePermissions = OverridePermissions;
                file.SourcePath = _sourcePathVM.VariablePath.ToList();
                file.DestinationPath = _destPathVM.VariablePath.ToList();
                file.Action = Action;
                file.FileSysIOType = IOType;
                file.IsPermanent = IsPermanent;
                file.ReplaceExisting = ReplaceExisting;
                file.Schedules = Schedules.ToList();
                if (!Design.IsDesignMode)
                    _coreManager.UpdateFileEvent(file);
            }
        }
    }
}
