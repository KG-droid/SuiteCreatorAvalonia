using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.CodeAnalysis;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels.EventCards
{
    internal partial class BrowserExtCardViewModel : EventCardWithPermanenceViewModelBase
    {
        private bool _isLoading = false;
        private SuiteCoreManager _coreManager;

        [ObservableProperty]
        private ExtAction? _action = ExtAction.Install;

        [ObservableProperty]
        private string? _extensionId;

        [ObservableProperty]
        private BrowserType? _browser = BrowserType.MSEdge;

        [ObservableProperty]
        private BrowserExtensionSource? _source = BrowserExtensionSource.MSEdgeWebStore;

        [ObservableProperty]
        private string? _extensionPath;

        partial void OnActionChanged(ExtAction? value)
        {
            CreateExtCardView();
        }

        public BrowserExtCardViewModel() : this(
            new BrowserExt { Id = Guid.NewGuid(), Action = ExtAction.Install, Browser = BrowserType.MSEdge },
            new SuiteCoreManager()
            )
        {
        }

        public BrowserExtCardViewModel(BrowserExt ext, SuiteCoreManager suiteCoreManager) : base(ext, suiteCoreManager)
        {
            _coreManager = suiteCoreManager;
            if (ext != null)
                LoadEvent(ext);
            CreateExtCardView();
        }

        private void CreateExtCardView()
        {
            Grid mainGrid = new Grid();
            CardInnerView = mainGrid;
            mainGrid.ColumnDefinitions = new ColumnDefinitions("Auto, *, Auto, Auto, Auto");

            // Action     
            ComboBox actionComboBox = new ComboBox();
            actionComboBox.ItemsSource = Enum.GetValues(typeof(ExtAction));
            actionComboBox.Bind(ComboBox.SelectedValueProperty, new Binding("Action"));
            actionComboBox.Background = Avalonia.Media.Brushes.Transparent;
            actionComboBox.SelectionChanged += HoldCardOpenAfterComboChange;
            Help.Annotate(actionComboBox, "Action", "Whether to Install or Uninstall this browser extension.");
            Grid.SetColumn(actionComboBox, 0);
            mainGrid.Children.Add(actionComboBox);

            // Extension ID
            TextBox extIDTextBox = new TextBox();
            extIDTextBox.Bind(TextBox.TextProperty, new Binding("ExtensionId"));
            extIDTextBox.PlaceholderText = "Extension ID";
            extIDTextBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            Help.Annotate(extIDTextBox, "Extension ID", "The store ID (or unique ID) of the browser extension to install or remove.");
            Grid.SetColumn(extIDTextBox, 1);
            mainGrid.Children.Add(extIDTextBox);

            // Browser
            ComboBox browserComboBox = new ComboBox();
            browserComboBox.ItemsSource = Enum.GetValues(typeof(BrowserType));
            browserComboBox.Bind(ComboBox.SelectedValueProperty, new Binding("Browser"));
            browserComboBox.Background = Avalonia.Media.Brushes.Transparent;
            browserComboBox.SelectionChanged += HoldCardOpenAfterComboChange;
            Help.Annotate(browserComboBox, "Browser", "Which browser this extension is installed into or removed from.");
            Grid.SetColumn(browserComboBox, 2);
            mainGrid.Children.Add(browserComboBox);

            if (Action == ExtAction.Install)
            {
                // Source
                ComboBox sourceComboBox = new ComboBox();
                sourceComboBox.ItemsSource = Enum.GetValues(typeof(BrowserExtensionSource));
                sourceComboBox.Bind(ComboBox.SelectedValueProperty, new Binding("Source"));
                sourceComboBox.Background = Avalonia.Media.Brushes.Transparent;
                sourceComboBox.SelectionChanged += HoldCardOpenAfterComboChange;
                Help.Annotate(sourceComboBox, "Source", "Where the extension is installed from: a browser web store, or a local CRX file.");
                Grid.SetColumn(sourceComboBox, 3);
                mainGrid.Children.Add(sourceComboBox);

                // Toggle Permanent
                ToggleButton permanentFileToggle = new();
                permanentFileToggle.Classes.Add("IconToggleButton");
                permanentFileToggle.Bind(ToggleButton.IsCheckedProperty, new Binding("IsPermanent"));
                permanentFileToggle.Tag = "DiamondStone";
                ToolTip.SetTip(permanentFileToggle, "If enabled, the Browser Extension won't be removed on Suite removal");
                Help.Annotate(permanentFileToggle, "Permanent", "If enabled, this browser extension won't be removed on Suite removal.");
                Grid.SetColumn(permanentFileToggle, 4);
                mainGrid.Children.Add(permanentFileToggle);
            }
        }

        partial void OnSourceChanged(BrowserExtensionSource? value)
        {
            if (value == BrowserExtensionSource.Local)
            {
                // Ext Path
                TextBox extPathTextBox = new TextBox();
                extPathTextBox.Bind(TextBox.TextProperty, new Binding("ExtensionPath"));
                extPathTextBox.PlaceholderText = "Extension CRX Path";
                Help.Annotate(extPathTextBox, "CRX path", "Path to the local .crx extension package file to install.");
                extPathTextBox.Name = "ExtPath_TextBox";
                extPathTextBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                Grid.SetColumn(extPathTextBox, 4);
                CardInnerView.Children.Add(extPathTextBox);

                Button extPathBrowseButton = new Button();
                string browseDiagTitle = "Select CRX extension file";
                extPathBrowseButton.Classes.Add("IconButton");
                extPathBrowseButton.Tag = "FolderOpen";
                extPathBrowseButton.Name = "ExtPathBrowse_Button";
                extPathBrowseButton.Margin = new Avalonia.Thickness(5, 0, 5, 0);
                extPathBrowseButton.Command = new RelayCommand(ExtensionBrowse);
                ToolTip.SetTip(extPathBrowseButton, browseDiagTitle);
                Help.Annotate(extPathBrowseButton, "Browse", browseDiagTitle + ".");
                Grid.SetColumn(extPathBrowseButton, 5);
                CardInnerView.Children.Add(extPathBrowseButton);
                CardInnerView.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto,*,Auto");
            }
            else
            {
                CardInnerView.Children
                    .Where(x => x.Name == "ExtPath_TextBox" || x.Name == "ExtPathBrowse_Button")
                    .ToList()
                    .ForEach(x => CardInnerView.Children.Remove(x));
                CardInnerView.ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto");
            }
        }

        private async void ExtensionBrowse()
        {
            IEnumerable<string> result = (await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Browse for a CRX extension", FileTypeFilter = SysIOPickerTypes.CRX }));
            if (result != null && result.Any())
            {
                ExtensionPath = result.First();
            }
        }

        public override void LoadEvent(EventCore eventCore)
        {
            if (eventCore is BrowserExt browseEx)
            {
                _isLoading = true;
                Action = browseEx.Action;
                ExtensionId = browseEx.ExtensionId;
                Browser = browseEx.Browser;
                Source = browseEx.Source;
                ExtensionPath = browseEx.ExtPath;
                Schedules.Clear();
                Schedules.AddRange(browseEx.Schedules);
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
                LinkedEvent = browseEx;
                _isLoading = false;
            }
        }

        public override void SaveEvent()
        {
            if (_isLoading) return;
            if (LinkedEvent is BrowserExt browserExt)
            {
                browserExt.Action = Action;
                browserExt.ExtensionId = ExtensionId;
                browserExt.Browser = Browser;
                browserExt.Source = Source;
                browserExt.ExtPath = ExtensionPath;
                browserExt.Schedules = Schedules.ToList();
                if (!Design.IsDesignMode)
                    _coreManager.UpdateExtensionEvent(browserExt);
            }
            ;
        }
    }
}
