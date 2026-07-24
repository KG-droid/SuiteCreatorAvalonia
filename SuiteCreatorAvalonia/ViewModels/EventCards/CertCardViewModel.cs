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
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels.EventCards
{
    internal partial class CertCardViewModel : EventCardWithPermanenceViewModelBase
    {
        private bool _isLoading = false;
        private SuiteCoreManager _coreManager;

        [ObservableProperty]
        private CertAction _action = CertAction.Add;

        [ObservableProperty]
        private CertStore _store = CertStore.TrustedRootCA;

        [ObservableProperty]
        private string? _filePath;

        [ObservableProperty]
        private string? _thumbPrint;

        [ObservableProperty]
        private string? _password;
        partial void OnActionChanged(CertAction value)
        {
            CreateRegCardView();
        }

        public CertCardViewModel() : this(
            new Cert { Action = CertAction.Add, Store = CertStore.TrustedRootCA },
            new SuiteCoreManager())
        {
        }

        public CertCardViewModel(Cert cert, SuiteCoreManager suiteCoreManager) : base(cert, suiteCoreManager)
        {
            _coreManager = suiteCoreManager;
            if (cert != null)
                LoadEvent(cert);
            else
                CreateRegCardView();
        }

        private void CreateRegCardView()
        {
            Grid mainGrid = new Grid();

            // Action
            ComboBox operationCombo = new ComboBox();
            operationCombo.ItemsSource = Enum.GetValues(typeof(CertAction));
            operationCombo.Bind(ComboBox.SelectedValueProperty, new Binding("Action"));
            operationCombo.Background = Avalonia.Media.Brushes.Transparent;
            operationCombo.SelectionChanged += HoldCardOpenAfterComboChange;
            operationCombo.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Help.Annotate(operationCombo, "Action", "Whether to Add a certificate to the store, or Remove one by thumbprint.");
            Grid.SetColumn(operationCombo, 0);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            mainGrid.Children.Add(operationCombo);

            // Cert Store     
            ComboBox storeComboBox = new ComboBox();
            storeComboBox.ItemsSource = Enum.GetValues(typeof(CertStore));
            storeComboBox.Bind(ComboBox.SelectedValueProperty, new Binding("Store"));
            storeComboBox.Background = Avalonia.Media.Brushes.Transparent;
            storeComboBox.SelectionChanged += HoldCardOpenAfterComboChange;
            storeComboBox.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            Help.Annotate(storeComboBox, "Certificate store", "Which Windows certificate store the certificate is added to or removed from, e.g. Trusted Root CA.");
            Grid.SetColumn(storeComboBox, 1);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            mainGrid.Children.Add(storeComboBox);

            // File Path
            if (Action == CertAction.Add)
            {
                // File Path
                TextBox textBox = new TextBox();
                textBox.Bind(TextBox.TextProperty, new Binding("FilePath"));
                textBox.PlaceholderText = "File Path to the certificate .cer file";
                textBox.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
                Help.Annotate(textBox, "Certificate file", "Path to the .cer/.pfx certificate file to install.");
                Grid.SetColumn(textBox, 2);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));
                mainGrid.Children.Add(textBox);

                Button browseButton = new Button();
                browseButton.Classes.Add("IconButton");
                browseButton.Tag = "FolderOpen";
                browseButton.Margin = new Avalonia.Thickness(5, 0, 0, 0);
                browseButton.Command = new RelayCommand(CertBrowse);
                ToolTip.SetTip(browseButton, "Browse for a certificate");
                Help.Annotate(browseButton, "Browse", "Browse for a certificate file.");
                Grid.SetColumn(browseButton, 3);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(browseButton);

                // Toggle Permanent
                ToggleButton permanentFileToggle = new();
                permanentFileToggle.Classes.Add("IconToggleButton");
                permanentFileToggle.Bind(ToggleButton.IsCheckedProperty, new Binding("IsPermanent"));
                permanentFileToggle.Tag = "DiamondStone";
                ToolTip.SetTip(permanentFileToggle, "If enabled, the Certificate won't be removed on Suite removal");
                Help.Annotate(permanentFileToggle, "Permanent", "If enabled, this certificate won't be removed on Suite removal.");
                Grid.SetColumn(permanentFileToggle, 4);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(permanentFileToggle);

                // Add Password Field
                Button addPasswordButton = new Button();
                addPasswordButton.Classes.Add("IconButton");
                addPasswordButton.Tag = "LockOpenPlus";
                addPasswordButton.Margin = new Avalonia.Thickness(5, 0, 0, 0);
                addPasswordButton.Command = new RelayCommand(() => AddPasswordField(mainGrid));
                ToolTip.SetTip(addPasswordButton, "Toggle Password to unlock the certificate");
                Help.Annotate(addPasswordButton, "Password", "Toggle a password field for certificates (like PFX) that need one to unlock.");
                Grid.SetColumn(addPasswordButton, 5);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(addPasswordButton);
                if (!string.IsNullOrWhiteSpace(Password))
                {
                    AddPasswordField(mainGrid);
                }
            }
            else
            {
                // Thumprint
                TextBox thumbTextBox = new TextBox();
                thumbTextBox.Bind(TextBox.TextProperty, new Binding("ThumbPrint"));
                thumbTextBox.PlaceholderText = "Thumbprint ID of the certificate";
                thumbTextBox.Margin = new Avalonia.Thickness(0, 0, 5, 0);
                Help.Annotate(thumbTextBox, "Thumbprint", "The thumbprint ID identifying which installed certificate to remove.");
                Grid.SetColumn(thumbTextBox, 2);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(thumbTextBox);
            }
            CardInnerView = mainGrid;
        }

        private async void CertBrowse()
        {
            IEnumerable<string>? result = (await this.OpenFileDialogAsync(new FilePickerOpenOptions() { AllowMultiple = false, Title = "Browse for a Certificate", FileTypeFilter = SysIOPickerTypes.Certificate }));
            if (result != null && result.Any())
            {
                FilePath = result.First();
            }
        }

        private void AddPasswordField(Grid mainGrid)
        {
            var passTxt = mainGrid.Children.Where(c => c.Tag != null && c.Tag.Equals("Pass")).FirstOrDefault();
            if (passTxt == null)
            {
                TextBox passwordTextBox = new TextBox();
                passwordTextBox.Tag = "Pass";
                passwordTextBox.Bind(TextBox.TextProperty, new Binding("Password"));
                passwordTextBox.PlaceholderText = "Password to unlock the certificate";
                Help.Annotate(passwordTextBox, "Password", "Password used to unlock the certificate file, if it needs one.");
                passwordTextBox.Margin = new Avalonia.Thickness(0, 0, 10, 0);
                passwordTextBox.MinWidth = 230;
                Grid.SetColumn(passwordTextBox, 5);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                mainGrid.Children.Add(passwordTextBox);
            }
            else
            {
                Password = null;
                mainGrid.Children.Remove(passTxt);
            }
        }

        public override void LoadEvent(EventCore eventCore)
        {
            _isLoading = true;
            if (eventCore is Cert cert)
            {
                Action = cert.Action;
                Store = cert.Store;
                FilePath = cert.FilePath;
                ThumbPrint = cert.Thumbprint;
                Password = cert.Password;
                Schedules.Clear();
                Schedules.AddRange(cert.Schedules);
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
                LinkedEvent = cert;
            }
            CreateRegCardView();
            _isLoading = false;
        }

        public override void SaveEvent()
        {
            if (_isLoading) return;
            if (LinkedEvent is Cert cert)
            {
                cert.Action = Action;
                cert.Store = Store;
                cert.FilePath = FilePath;
                cert.Thumbprint = ThumbPrint;
                cert.Password = Password;
                cert.Schedules = Schedules.ToList();
                if (!Design.IsDesignMode)
                    _coreManager.UpdateCertEvent(cert);
            }
        }
    }
}
