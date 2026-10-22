using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels.EventCards
{
    internal partial class FontCardViewModel : EventCardWithPermanenceViewModelBase
    {
        private bool _isLoading = false;
        private SuiteCoreManager _coreManager;

        [ObservableProperty]
        private FontAction _action = FontAction.Install;

        [ObservableProperty]
        private string? _fontPath;

        [ObservableProperty]
        private string? _fontFileName;

        partial void OnActionChanged(FontAction value)
        {
            CreateFontCardView();
        }

        public FontCardViewModel() : this(
            new SystemFont { Action = FontAction.Install },
            new SuiteCoreManager())
        {
        }

        public FontCardViewModel(SystemFont font, SuiteCoreManager suiteCoreManager) : base(font, suiteCoreManager)
        {
            _coreManager = suiteCoreManager;
            if (font != null)
                LoadEvent(font);
            else
                CreateFontCardView();
        }

        private void CreateFontCardView()
        {
            Grid mainGrid = new Grid();
            mainGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Auto));

            // Action
            ComboBox operationCombo = new ComboBox();
            operationCombo.ItemsSource = Enum.GetValues(typeof(FontAction));
            operationCombo.Bind(ComboBox.SelectedValueProperty, new Binding("Action"));
            operationCombo.Background = Avalonia.Media.Brushes.Transparent;
            operationCombo.SelectionChanged += HoldCardOpenAfterComboChange;
            operationCombo.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Help.Annotate(operationCombo, "Action", "Whether to Install a font file (or every font in a folder) for all users, or Remove installed fonts by their file name.");
            Grid.SetColumn(operationCombo, 0);
            Grid.SetRow(operationCombo, 0);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            mainGrid.Children.Add(operationCombo);

            if (Action == FontAction.Install)
            {
                // Font file or folder path
                TextBox textBox = new TextBox();
                textBox.Bind(TextBox.TextProperty, new Binding("FontPath"));
                textBox.PlaceholderText = "Path to a font file (.ttf, .otf, .ttc, .otc), or a folder of fonts to install them all";
                textBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                ToolTip.SetTip(textBox, "A single font file, or a folder. If a folder is given, every font file in it (including sub folders) is bundled and installed.");
                Help.Annotate(textBox, "Font file or folder", "Path to a font file, or a folder of fonts. The font files are bundled into the Suite and installed for all users on the machine.");
                Grid.SetColumn(textBox, 1);
                Grid.SetRow(textBox, 0);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                mainGrid.Children.Add(textBox);

                Button browseFileButton = new Button();
                browseFileButton.Classes.Add("IconButton");
                browseFileButton.Tag = "File";
                browseFileButton.Margin = new Avalonia.Thickness(5, 0, 0, 0);
                browseFileButton.Command = new RelayCommand(FontFileBrowse);
                ToolTip.SetTip(browseFileButton, "Browse for a font file");
                Help.Annotate(browseFileButton, "Browse for a file", "Browse for a single font file.");
                Grid.SetColumn(browseFileButton, 2);
                Grid.SetRow(browseFileButton, 0);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(browseFileButton);

                Button browseFolderButton = new Button();
                browseFolderButton.Classes.Add("IconButton");
                browseFolderButton.Tag = "Folder";
                browseFolderButton.Margin = new Avalonia.Thickness(5, 0, 0, 0);
                browseFolderButton.Command = new RelayCommand(FontFolderBrowse);
                ToolTip.SetTip(browseFolderButton, "Browse for a folder of fonts");
                Help.Annotate(browseFolderButton, "Browse for a folder", "Browse for a folder, every font file in it will be installed.");
                Grid.SetColumn(browseFolderButton, 3);
                Grid.SetRow(browseFolderButton, 0);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(browseFolderButton);

                // Toggle Permanent
                ToggleButton permanentToggle = new();
                permanentToggle.Classes.Add("IconToggleButton");
                permanentToggle.Bind(ToggleButton.IsCheckedProperty, new Binding("IsPermanent"));
                permanentToggle.Tag = "DiamondStone";
                ToolTip.SetTip(permanentToggle, "If enabled, the font(s) won't be removed on Suite removal");
                Help.Annotate(permanentToggle, "Permanent", "If enabled, these fonts won't be removed on Suite removal.");
                Grid.SetColumn(permanentToggle, 4);
                Grid.SetRow(permanentToggle, 0);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(permanentToggle);
            }
            else
            {
                // File name(s) of the fonts to remove
                TextBox fileNameTextBox = new TextBox();
                fileNameTextBox.Bind(TextBox.TextProperty, new Binding("FontFileName"));
                fileNameTextBox.PlaceholderText = "File name(s) of the font(s) to remove, e.g. myfont.ttf (use commas to separate several)";
                fileNameTextBox.MinWidth = 400;
                fileNameTextBox.Margin = new Avalonia.Thickness(0, 0, 5, 0);
                Help.Annotate(fileNameTextBox, "Font file name(s)", "The file name(s) of the installed font(s) to remove from the Windows Fonts folder, e.g. myfont.ttf. Separate several with commas.");
                Grid.SetColumn(fileNameTextBox, 1);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(fileNameTextBox);
            }
            CardInnerView = mainGrid;
        }

        private async void FontFileBrowse()
        {
            IEnumerable<string>? result = await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Browse for a font file", FileTypeFilter = SysIOPickerTypes.Fonts });
            if (result != null && result.Any())
            {
                FontPath = result.First();
            }
        }

        private async void FontFolderBrowse()
        {
            IEnumerable<string>? result = await this.OpenFolderDialogAsync(new FolderPickerOpenOptions() { AllowMultiple = false, Title = "Browse for a folder of fonts" });
            if (result != null && result.Any())
            {
                FontPath = result.First();
            }
        }

        public override void LoadEvent(EventCore eventCore)
        {
            _isLoading = true;
            if (eventCore is SystemFont font)
            {
                Action = font.Action;
                FontPath = font.FontPath;
                FontFileName = font.FontFileName;
                IsPermanent = font.IsPermanent;
                Schedules.Clear();
                Schedules.AddRange(font.Schedules);
                // Ensure that the EventStage and Condition in each Schedule is the same instance as in SuiteStages/SuiteConditions for the ComboBox binding to work correctly.
                NormalizeSchedules();
                LinkedEvent = font;
            }
            CreateFontCardView();
            _isLoading = false;
        }

        public override void SaveEvent()
        {
            if (_isLoading) return;
            if (LinkedEvent is SystemFont font)
            {
                font.Action = Action;
                font.FontPath = FontPath;
                font.FontFileName = FontFileName;
                font.IsPermanent = IsPermanent;
                font.Schedules = Schedules.ToList();
                if (!Design.IsDesignMode)
                    _coreManager.UpdateFontEvent(font);
            }
        }
    }
}
