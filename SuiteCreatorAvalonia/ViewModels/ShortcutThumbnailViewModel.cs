using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.Tools;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class ShortcutThumbnailViewModel : ViewModelBase
    {
        private Border _iconPreviewBorder;
        private string? _defaultBrowserPath = WebBrowser.GetDefaultBrowserPath() ?? string.Empty;

        [ObservableProperty]
        public Shortcut? _linkedShortcutEvent;

        [ObservableProperty]
        public ShortcutAction? _shortAction = ShortcutAction.Create;

        [ObservableProperty]
        public ShortcutType? _shortType = ShortcutType.Program;

        [ObservableProperty]
        [Required(ErrorMessage = "Target is required.")]
        public string? _target;

        [ObservableProperty]
        public string? _arguments;

        [ObservableProperty]
        public string? _name = "New Shortcut";

        [ObservableProperty]
        public List<ShortcutPlacement>? _shortcutPlacementList = new List<ShortcutPlacement>() { ShortcutPlacement.Desktop };

        [ObservableProperty]
        public bool _desktopShortcut = false;

        [ObservableProperty]
        public bool _startmenuShortcut = true;

        [ObservableProperty]
        public string? _workingDIR;

        [ObservableProperty]
        public Contexts? _context = Contexts.System;

        [ObservableProperty]
        public bool _isManualIcon = false;

        [ObservableProperty]
        public bool _isPathIcon = false;

        [ObservableProperty]
        public string? _iconPath;

        [ObservableProperty]
        public int? _iconIndex = 0;

        [ObservableProperty]
        public Grid _flyoutGrid = new Grid();

        [ObservableProperty]
        public bool _isRemoveMode = false;

        [ObservableProperty]
        public bool _isMarkedForDeletion = false;

        [ObservableProperty]
        public Bitmap? _selectedIconPreview;

        [ObservableProperty]
        public Bitmap? _thumbIcon;

        public bool HasThumbIcon => SelectedIconPreview != null;

        [ObservableProperty]
        public ObservableCollection<Bitmap> _allIconPreviews = new();

        [ObservableProperty]
        public string _iconPreviewText;

        public bool IsPreviewUnavailable => SelectedIconPreview == null;

        partial void OnIconIndexChanged(int? value)
        {
            if (value == null)
                IconIndex = 0;
            if (AllIconPreviews.Any() && AllIconPreviews.Count - 1 >= (int)IconIndex)
            {
                SelectedIconPreview = AllIconPreviews.ElementAt((int)IconIndex);
            }
            else
            {
                SelectedIconPreview = null;
                IconPreviewText = "Preview Unavailable";
            }
        }

        partial void OnSelectedIconPreviewChanged(Bitmap? value)
        {
            OnPropertyChanged(nameof(IsPreviewUnavailable));
            OnPropertyChanged(nameof(HasThumbIcon));
            if (value != null)
                ThumbIcon = SelectedIconPreview;
            else
                ThumbIcon = null;
        }

        partial void OnTargetChanged(string? value)
        {
            try
            {
                if (ShortType == ShortcutType.Program)
                {
                    AllIconPreviews.Clear();
                    IconIndex = 0;
                    if (!string.IsNullOrWhiteSpace(value) && Path.Exists(value))
                    {
                        var icons = WindowsShell.ExtractAllIconsFromExecutable(value, true);
                        if (icons != null)
                            AllIconPreviews.AddRange(icons);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warning($"Failed to extract icons from: {value}, error: {ex.Message}", "Shortcuts");
                IconPreviewText = "Invalid/Corrupted Icon";
            }
        }

        partial void OnIsPathIconChanged(bool value)
        {
            CreateFlyOutView();
            if (IsManualIcon)
            {
                if (value)
                {
                    // Path Icon type
                    if (!string.IsNullOrWhiteSpace(IconPath) && Path.Exists(IconPath))
                    {
                        try
                        {
                            SelectedIconPreview = ImageLoader.Get(new Uri(IconPath));
                        }
                        catch
                        {
                            SelectedIconPreview = null;
                            IconPreviewText = "Invalid/Corrupted Icon";
                        }
                    }
                    else
                    {
                        SelectedIconPreview = null;
                        IconPreviewText = "Preview Unavailable";
                    }
                }
                else
                {
                    // Index Icon type
                    if (AllIconPreviews.Any() && AllIconPreviews.Count - 1 >= (int)IconIndex)
                    {
                        SelectedIconPreview = AllIconPreviews.ElementAt((int)IconIndex);
                    }
                    else
                    {
                        SelectedIconPreview = null;
                        IconPreviewText = "Preview Unavailable";
                    }
                }
            }
            else
            {
                SelectedIconPreview = null;
                IconPreviewText = "Preview Unavailable";
            }
        }

        partial void OnIconPathChanged(string? value)
        {
            if (IsManualIcon && IsPathIcon && !string.IsNullOrWhiteSpace(value) && Path.Exists(value))
            {
                try
                {
                    SelectedIconPreview = ImageLoader.Get(new Uri(value));
                }
                catch
                {
                    SelectedIconPreview = null;
                    IconPreviewText = "Invalid/Corrupted Icon";
                }
                return;
            }
            SelectedIconPreview = null;
            IconPreviewText = "Preview Unavailable";
        }

        partial void OnShortTypeChanged(ShortcutType? value)
        {
            Target = string.Empty;
            if (value == ShortcutType.Web)
            {
                if (IsPathIcon)
                {
                    OnIconPathChanged(IconPath);
                }
                else
                {
                    _defaultBrowserPath = WebBrowser.GetDefaultBrowserPath() ?? string.Empty;
                    if (!string.IsNullOrWhiteSpace(_defaultBrowserPath) && Path.Exists(_defaultBrowserPath))
                    {
                        var icons = WindowsShell.ExtractAllIconsFromExecutable(_defaultBrowserPath, true);
                        if (icons != null)
                        {
                            AllIconPreviews.Clear();
                            AllIconPreviews.AddRange(icons);
                            SelectedIconPreview = AllIconPreviews.FirstOrDefault();
                        }
                    }
                }
            }
            else
            {
                OnTargetChanged(Target);
                OnIconPathChanged(IconPath);
            }
            CreateFlyOutView();
        }

        partial void OnShortActionChanged(ShortcutAction? value)
        {
            CreateFlyOutView();
        }

        partial void OnIsManualIconChanged(bool value)
        {
            CreateFlyOutView();
        }

        public ShortcutThumbnailViewModel()
        {
            Setup_iconPreviewBorder();
            CreateFlyOutView();
            AllIconPreviews.CollectionChanged += (sender, e) =>
            {
                SelectedIconPreview = AllIconPreviews.FirstOrDefault();
            };
        }

        private void Setup_iconPreviewBorder()
        {
            _iconPreviewBorder = new();
            _iconPreviewBorder.Width = 60;
            _iconPreviewBorder.Height = 60;
            _iconPreviewBorder.VerticalAlignment = VerticalAlignment.Center;
            Grid iconGrid = new Grid();
            IconPreviewText = "Preview Unavailable";
            TextBlock notavailableText = new TextBlock
            {
                TextWrapping = TextWrapping.Wrap,
                FontSize = 9,
                VerticalAlignment = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            notavailableText.Bind(TextBlock.TextProperty, new Binding("IconPreviewText"));
            notavailableText.Bind(TextBlock.IsVisibleProperty, new Binding("IsPreviewUnavailable"));
            iconGrid.Children.Add(notavailableText);
            Image iconImage = new Image();
            iconImage.Bind(Image.SourceProperty, new Binding("SelectedIconPreview"));
            iconGrid.Children.Add(iconImage);
            _iconPreviewBorder.Child = iconGrid;
        }

        private void CreateFlyOutView()
        {
            FlyoutGrid = new Grid();
            FlyoutGrid.ColumnDefinitions = new ColumnDefinitions("Auto,10,*,Auto,30");
            FlyoutGrid.RowDefinitions = new RowDefinitions("Auto,10,Auto,10,Auto");

            Grid? iconPreviewParent = _iconPreviewBorder.Parent != null ? _iconPreviewBorder.Parent as Grid : null;
            iconPreviewParent?.Children.Remove(_iconPreviewBorder);

            // Action
            TextBlock actionTextBlock = new TextBlock();
            actionTextBlock.Text = "Action:";
            actionTextBlock.VerticalAlignment = VerticalAlignment.Center;
            actionTextBlock.Classes.Add("FieldName");
            Grid.SetColumn(actionTextBlock, 0);
            Grid.SetRow(actionTextBlock, 0);
            FlyoutGrid.Children.Add(actionTextBlock);

            ComboBox actionCombo = new ComboBox();
            actionCombo.ItemsSource = Enum.GetValues(typeof(ShortcutAction));
            actionCombo.Bind(ComboBox.SelectedValueProperty, new Binding("ShortAction"));
            actionCombo.Background = Brushes.Transparent;
            actionCombo.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(actionCombo, 2);
            Grid.SetRow(actionCombo, 0);
            FlyoutGrid.Children.Add(actionCombo);

            int nextRow = 2;

            if (ShortAction == ShortcutAction.Create)
            {
                // Type
                TextBlock typeTextBlock = new TextBlock();
                typeTextBlock.Text = "Type:";
                typeTextBlock.VerticalAlignment = VerticalAlignment.Center;
                typeTextBlock.Classes.Add("FieldName");
                Grid.SetColumn(typeTextBlock, 0);
                Grid.SetRow(typeTextBlock, nextRow);
                FlyoutGrid.Children.Add(typeTextBlock);

                ComboBox typeCombo = new ComboBox();
                typeCombo.ItemsSource = Enum.GetValues(typeof(ShortcutType));
                typeCombo.Bind(ComboBox.SelectedValueProperty, new Binding("ShortType"));
                typeCombo.Background = Brushes.Transparent;
                typeCombo.VerticalAlignment = VerticalAlignment.Center;
                Grid.SetColumn(typeCombo, 2);
                Grid.SetRow(typeCombo, nextRow);
                nextRow = nextRow + 2;
                FlyoutGrid.Children.Add(typeCombo);

                // Target
                TextBlock targetTextBlock = new TextBlock();
                targetTextBlock.Text = "Target:";
                targetTextBlock.VerticalAlignment = VerticalAlignment.Center;
                targetTextBlock.Classes.Add("FieldName");
                Grid.SetColumn(targetTextBlock, 0);
                Grid.SetRow(targetTextBlock, nextRow);
                FlyoutGrid.Children.Add(targetTextBlock);

                TextBox targetTextBox = new TextBox();
                targetTextBox.Bind(TextBox.TextProperty, new Binding("Target"));
                targetTextBox.VerticalAlignment = VerticalAlignment.Center;
                targetTextBox.TextWrapping = TextWrapping.Wrap;
                targetTextBox.LostFocus += (sender, e) => ValidateTarget(sender as TextBox);
                targetTextBox.TextWrapping = TextWrapping.NoWrap;
                Grid.SetColumn(targetTextBox, 2);
                Grid.SetRow(targetTextBox, nextRow);
                FlyoutGrid.Children.Add(targetTextBox);

                if (ShortType == ShortcutType.Program)
                {
                    Button targetBrowseButton = new Button();
                    targetBrowseButton.Classes.Add("IconButton");
                    targetBrowseButton.Tag = "Target";
                    targetBrowseButton.Command = new RelayCommand(async () => await BrowseForTargetPath());
                    Grid.SetColumn(targetBrowseButton, 4);
                    Grid.SetRow(targetBrowseButton, nextRow);
                    FlyoutGrid.Children.Add(targetBrowseButton);
                }
                nextRow = nextRow + 2;


                if (ShortType == ShortcutType.Program)
                {
                    // Args
                    TextBlock argsTextBlock = new TextBlock();
                    argsTextBlock.Text = "Arguments:";
                    argsTextBlock.VerticalAlignment = VerticalAlignment.Center;
                    argsTextBlock.Classes.Add("FieldName");
                    Grid.SetColumn(argsTextBlock, 0);
                    Grid.SetRow(argsTextBlock, nextRow);
                    FlyoutGrid.Children.Add(argsTextBlock);

                    TextBox argsTextBox = new TextBox();
                    argsTextBox.Bind(TextBox.TextProperty, new Binding("Arguments"));
                    argsTextBox.VerticalAlignment = VerticalAlignment.Center;
                    argsTextBox.TextWrapping = TextWrapping.Wrap;
                    argsTextBox.PlaceholderText = "Optional";
                    argsTextBox.TextWrapping = TextWrapping.NoWrap;
                    Grid.SetColumn(argsTextBox, 2);
                    Grid.SetRow(argsTextBox, nextRow);
                    FlyoutGrid.Children.Add(argsTextBox);
                    FlyoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(10)));
                    FlyoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    nextRow = nextRow + 2;

                    // Working DIR
                    TextBlock workingDIRTextBlock = new TextBlock();
                    workingDIRTextBlock.Text = "Working DIR:";
                    workingDIRTextBlock.VerticalAlignment = VerticalAlignment.Center;
                    workingDIRTextBlock.Classes.Add("FieldName");
                    Grid.SetColumn(workingDIRTextBlock, 0);
                    Grid.SetRow(workingDIRTextBlock, nextRow);
                    FlyoutGrid.Children.Add(workingDIRTextBlock);

                    TextBox workingDIRTextBox = new TextBox();
                    workingDIRTextBox.Bind(TextBox.TextProperty, new Binding("WorkingDIR"));
                    workingDIRTextBox.VerticalAlignment = VerticalAlignment.Center;
                    workingDIRTextBox.TextWrapping = TextWrapping.Wrap;
                    workingDIRTextBox.LostFocus += (sender, e) => ValidateWorkingDIR(sender as TextBox);
                    workingDIRTextBox.PlaceholderText = "Optional";
                    workingDIRTextBox.TextWrapping = TextWrapping.NoWrap;
                    Grid.SetColumn(workingDIRTextBox, 2);
                    Grid.SetRow(workingDIRTextBox, nextRow);
                    FlyoutGrid.Children.Add(workingDIRTextBox);


                    Button workingDIRBrowseButton = new Button();
                    workingDIRBrowseButton.Classes.Add("IconButton");
                    workingDIRBrowseButton.Tag = "Folder";
                    workingDIRBrowseButton.Command = new RelayCommand(async () => await BrowseForWorkingDIR());
                    Grid.SetColumn(workingDIRBrowseButton, 4);
                    Grid.SetRow(workingDIRBrowseButton, nextRow);
                    FlyoutGrid.Children.Add(workingDIRBrowseButton);

                    FlyoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(10)));
                    FlyoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                    nextRow = nextRow + 2;
                }
            }

            // Name
            TextBlock nameTextBlock = new TextBlock();
            nameTextBlock.Text = "Name:";
            nameTextBlock.VerticalAlignment = VerticalAlignment.Center;
            nameTextBlock.Classes.Add("FieldName");
            Grid.SetColumn(nameTextBlock, 0);
            Grid.SetRow(nameTextBlock, nextRow);
            FlyoutGrid.Children.Add(nameTextBlock);

            TextBox nameTextBox = new TextBox();
            nameTextBox.Bind(TextBox.TextProperty, new Binding("Name"));
            nameTextBox.VerticalAlignment = VerticalAlignment.Center;
            nameTextBox.TextWrapping = TextWrapping.Wrap;
            nameTextBox.GotFocus += (sender, e) => { if ((sender as TextBox).Text == "New Shortcut") { (sender as TextBox).Text = ""; } };
            nameTextBox.LostFocus += (sender, e) => { if (string.IsNullOrWhiteSpace((sender as TextBox).Text)) { (sender as TextBox).Text = "New Shortcut"; } };
            nameTextBox.TextWrapping = TextWrapping.NoWrap;
            Grid.SetColumn(nameTextBox, 2);
            Grid.SetRow(nameTextBox, nextRow);
            FlyoutGrid.Children.Add(nameTextBox);
            FlyoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(10)));
            FlyoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            nextRow = nextRow + 2;

            // Placement
            TextBlock placementTextBlock = new TextBlock();
            placementTextBlock.Text = "Placement:";
            placementTextBlock.VerticalAlignment = VerticalAlignment.Center;
            placementTextBlock.Classes.Add("FieldName");
            Grid.SetColumn(placementTextBlock, 0);
            Grid.SetRow(placementTextBlock, nextRow);
            FlyoutGrid.Children.Add(placementTextBlock);

            CheckBox desktopCheckBox = new CheckBox();
            desktopCheckBox.Bind(CheckBox.IsCheckedProperty, new Binding("DesktopShortcut"));
            desktopCheckBox.Content = "Desktop";
            desktopCheckBox.VerticalAlignment = VerticalAlignment.Center;

            CheckBox startCheckBox = new CheckBox();
            startCheckBox.Bind(CheckBox.IsCheckedProperty, new Binding("StartmenuShortcut"));
            startCheckBox.Content = "Start Menu";
            startCheckBox.VerticalAlignment = VerticalAlignment.Center;

            StackPanel placementStackPanel = new StackPanel();
            placementStackPanel.Orientation = Orientation.Horizontal;
            placementStackPanel.Spacing = 10;
            Grid.SetColumn(placementStackPanel, 2);
            Grid.SetRow(placementStackPanel, nextRow);
            nextRow = nextRow + 2;
            placementStackPanel.Children.Add(desktopCheckBox);
            placementStackPanel.Children.Add(startCheckBox);
            FlyoutGrid.Children.Add(placementStackPanel);
            FlyoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(10)));
            FlyoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            // Context
            TextBlock contextTextBlock = new TextBlock();
            contextTextBlock.Text = "Context:";
            contextTextBlock.VerticalAlignment = VerticalAlignment.Center;
            contextTextBlock.Classes.Add("FieldName");
            Grid.SetColumn(contextTextBlock, 0);
            Grid.SetRow(contextTextBlock, nextRow);
            FlyoutGrid.Children.Add(contextTextBlock);

            ComboBox contextComboBox = new ComboBox();
            contextComboBox.ItemsSource = Enum.GetValues(typeof(Contexts));
            contextComboBox.Bind(ComboBox.SelectedValueProperty, new Binding("Context"));
            contextComboBox.Background = Brushes.Transparent;
            contextComboBox.VerticalAlignment = VerticalAlignment.Center;
            Grid.SetColumn(contextComboBox, 2);
            Grid.SetRow(contextComboBox, nextRow);
            nextRow = nextRow + 2;
            FlyoutGrid.Children.Add(contextComboBox);
            FlyoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(10)));
            FlyoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            if (ShortAction == ShortcutAction.Create)
            {
                // Icon Type
                TextBlock isManualIconTextBlock = new();
                isManualIconTextBlock.Text = "Icon:";
                isManualIconTextBlock.VerticalAlignment = VerticalAlignment.Center;
                isManualIconTextBlock.Classes.Add("FieldName");
                FlyoutGrid.Children.Add(isManualIconTextBlock);
                Grid.SetColumn(isManualIconTextBlock, 0);
                Grid.SetRow(isManualIconTextBlock, nextRow);

                StackPanel iconStack = new();
                iconStack.Orientation = Orientation.Horizontal;
                iconStack.Spacing = 10;
                Grid.SetColumn(iconStack, 2);
                Grid.SetRow(iconStack, nextRow);
                FlyoutGrid.Children.Add(iconStack);

                ToggleSwitch siManualIconToggle = new();
                siManualIconToggle.Bind(CheckBox.IsCheckedProperty, new Binding("IsManualIcon"));
                siManualIconToggle.Background = Brushes.Transparent;
                siManualIconToggle.OffContent = "Auto";
                siManualIconToggle.OnContent = "Manual";
                siManualIconToggle.VerticalAlignment = VerticalAlignment.Center;
                iconStack.Children.Add(siManualIconToggle);

                FlyoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(10)));
                FlyoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                nextRow = nextRow + 2;

                if (IsManualIcon)
                {
                    ToggleSwitch iconTypeToggle = new();
                    iconTypeToggle.Bind(CheckBox.IsCheckedProperty, new Binding("IsPathIcon"));
                    iconTypeToggle.Background = Brushes.Transparent;
                    iconTypeToggle.OffContent = "Index";
                    iconTypeToggle.OnContent = "Path";
                    iconTypeToggle.VerticalAlignment = VerticalAlignment.Center;
                    iconStack.Children.Add(iconTypeToggle);

                    FlyoutGrid.Children.Add(_iconPreviewBorder);
                    Grid.SetColumn(_iconPreviewBorder, 0);
                    Grid.SetRow(_iconPreviewBorder, nextRow);

                    if (IsPathIcon)
                    {
                        // Icon Path
                        TextBox iconPathTextBox = new();
                        iconPathTextBox.PlaceholderText = "Path to Ico file";
                        iconPathTextBox.VerticalAlignment = VerticalAlignment.Center;
                        iconPathTextBox.Classes.Add("FieldName");
                        iconPathTextBox.Bind(TextBox.TextProperty, new Binding("IconPath"));
                        iconPathTextBox.TextWrapping = TextWrapping.NoWrap;
                        Grid.SetColumn(iconPathTextBox, 2);
                        Grid.SetRow(iconPathTextBox, nextRow);
                        FlyoutGrid.Children.Add(iconPathTextBox);

                        Button iconPathBrowseButton = new();
                        iconPathBrowseButton.Classes.Add("IconButton");
                        iconPathBrowseButton.Tag = "FileImagePlus";
                        iconPathBrowseButton.Command = new RelayCommand(async () => await BrowseForIcoFile());
                        Grid.SetColumn(iconPathBrowseButton, 4);
                        Grid.SetRow(iconPathBrowseButton, nextRow);
                        FlyoutGrid.Children.Add(iconPathBrowseButton);
                    }
                    else
                    {
                        NumericUpDown iconIndexNumberic = new();
                        iconIndexNumberic.FormatString = "0";
                        iconIndexNumberic.Minimum = 0;
                        // Prevent manual text input in NumericUpDown
                        iconIndexNumberic.AddHandler(TextBox.KeyDownEvent, (sender, e) =>
                        {
                            e.Handled = true;
                        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                        iconIndexNumberic.AddHandler(TextBox.TextInputEvent, (sender, e) =>
                        {
                            // Prevent any text input (including numbers) in the NumericUpDown's TextBox
                            e.Handled = true;
                        }, Avalonia.Interactivity.RoutingStrategies.Tunnel);
                        iconIndexNumberic.VerticalAlignment = VerticalAlignment.Center;
                        iconIndexNumberic.Bind(NumericUpDown.ValueProperty, new Binding("IconIndex"));
                        Grid.SetColumn(iconIndexNumberic, 2);
                        Grid.SetRow(iconIndexNumberic, nextRow);
                        FlyoutGrid.Children.Add(iconIndexNumberic);
                    }

                    FlyoutGrid.RowDefinitions.Add(new RowDefinition(new GridLength(10)));
                    FlyoutGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
                }
            }
        }

        private void ValidateTarget(TextBox textBox)
        {
            if (string.IsNullOrWhiteSpace(textBox.Text)) { return; }
            if (ShortType == ShortcutType.Program)
            {
                if (!Path.IsPathRooted(textBox.Text))
                {
                    textBox.Text = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), textBox.Text);
                }
            }
            else
            {
                if (!Regex.IsMatch(textBox.Text, @"^www\."))
                {
                    textBox.Text = $"www.{textBox.Text}";
                }
                if (Regex.IsMatch(textBox.Text, @"^www\."))
                {
                    textBox.Text = $"https://{textBox.Text}";
                }
            }
        }

        private void ValidateWorkingDIR(TextBox textBox)
        {
            if (string.IsNullOrWhiteSpace(textBox.Text)) { return; }
            if (!Path.IsPathRooted(textBox.Text))
            {
                textBox.Text = Path.Combine(System.Environment.GetFolderPath(System.Environment.SpecialFolder.ProgramFiles), textBox.Text);
            }
        }

        public void LoadEvent(Shortcut shortcut)
        {
            if (shortcut == null) return;
            ShortAction = shortcut?.ShortAction;
            ShortType = shortcut?.ShortcutType;
            Target = shortcut?.Target?.OriginalString;
            Arguments = shortcut?.Arguments;
            Name = shortcut?.Name;
            DesktopShortcut = shortcut?.PlacementList?.Contains(ShortcutPlacement.Desktop) ?? false;
            StartmenuShortcut = shortcut?.PlacementList?.Contains(ShortcutPlacement.StartMenu) ?? true;
            WorkingDIR = shortcut?.WorkingDIR?.OriginalString;
            Context = shortcut?.Context;
            IsManualIcon = shortcut.IsManualIcon;
            IconPath = shortcut?.IconPath;
            IconIndex = (int)shortcut?.IconIndex;
            IsPathIcon = shortcut.IsPathIcon;
            LinkedShortcutEvent = shortcut;
        }

        private async Task BrowseForTargetPath()
        {
            IEnumerable<string>? result = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Select a target file", FileTypeFilter = SysIOPickerTypes.Any });
            if (null != result && result.Any())
                Target = result.First();
        }

        private async Task BrowseForIcoFile()
        {
            IEnumerable<string>? result = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Select an Ico file", FileTypeFilter = SysIOPickerTypes.Icon });
            if (null != result && result.Any())
                IconPath = result.First();
        }

        private async Task BrowseForWorkingDIR()
        {
            IEnumerable<string>? result = await this.OpenFolderDialogAsync(new FolderPickerOpenOptions() { AllowMultiple = false, Title = "Select a Working Directory" });
            if (null != result && result.Any())
                WorkingDIR = result.First();
        }

        public Shortcut? GetEvent()
        {
            if (LinkedShortcutEvent == null) return null;
            List<ShortcutPlacement> shortPlacementsList = new List<ShortcutPlacement>();
            if (DesktopShortcut)
            {
                shortPlacementsList.Add(ShortcutPlacement.Desktop);
            }
            if (StartmenuShortcut)
            {
                shortPlacementsList.Add(ShortcutPlacement.StartMenu);
            }
            LinkedShortcutEvent.ShortAction = ShortAction;
            LinkedShortcutEvent.ShortcutType = ShortType;
            LinkedShortcutEvent.Target = Uri.TryCreate(Target, UriKind.Absolute, out var targetResult) ? targetResult : null;
            LinkedShortcutEvent.Arguments = Arguments;
            LinkedShortcutEvent.Name = Name;
            LinkedShortcutEvent.PlacementList = shortPlacementsList;
            LinkedShortcutEvent.WorkingDIR = Uri.TryCreate(WorkingDIR, UriKind.Absolute, out var workingDIRResult) ? workingDIRResult : null;
            LinkedShortcutEvent.Context = Context;
            LinkedShortcutEvent.IsManualIcon = IsManualIcon;
            LinkedShortcutEvent.IconPath = IconPath;
            LinkedShortcutEvent.IconIndex = (int)IconIndex;
            LinkedShortcutEvent.IsPathIcon = IsPathIcon;
            return LinkedShortcutEvent;
        }

        [RelayCommand]
        public void MarkForDeletion()
        {
            IsMarkedForDeletion = !IsMarkedForDeletion;
        }
    }
}
