using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Platform.Storage;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Material.Icons.Avalonia;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.ViewModels;
using SuiteCreatorAvalonia.Views;
using SuiteTools;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FontWeight = Avalonia.Media.FontWeight;
using MSIx = SuiteTools.MSIx;

namespace SuiteCreatorAvalonia.ViewModels.RuleBuilder
{
    internal partial class RuleCardViewModel : RuleCardBaseViewModel
    {
        private bool _configuringCard = false;
        private bool _isLoading = false;

        [ObservableProperty]
        private HorizontalAlignment _cardHorizontalAlignment = HorizontalAlignment.Center;

        [ObservableProperty]
        private string? _name;

        [ObservableProperty]
        private RuleType _ruleType = RuleType.MSI;

        [ObservableProperty]
        private string? _base;

        [ObservableProperty]
        private TextDocument _script = new();

        [ObservableProperty]
        private Architecture? _architecture = Enums.Architecture.x86;

        [ObservableProperty]
        private Contexts? _context = Contexts.System;

        [ObservableProperty]
        private DetectionTypes _detectionType = DetectionTypes.Version;

        [ObservableProperty]
        private ComparatorPlus? _comparator;

        [ObservableProperty]
        private string? _comparatorProperty;

        [ObservableProperty]
        private string? _comparatorValue;

        [ObservableProperty]
        private Grid _cardInnerView = new Grid();

        [ObservableProperty]
        private bool _isSelected = false;

        [ObservableProperty]
        private bool _isBinButtonVisible = false;

        [ObservableProperty]
        private PathVarsTextBoxViewModel _fileBasePathVM = new() { IsAddFileButtonVisible = false };

        partial void OnScriptChanged(TextDocument value)
        {
            Script.TextChanged += (s, e) =>
            {
                OnPropertyChanged(nameof(Script));
            };
        }

        partial void OnDetectionTypeChanged(DetectionTypes oldValue, DetectionTypes newValue)
        {
            if (!_isLoading)
            {
                if (
                    ((oldValue == DetectionTypes.Installed || oldValue == DetectionTypes.NotInstalled) && newValue == DetectionTypes.Version) ||
                    ((newValue == DetectionTypes.Installed || newValue == DetectionTypes.NotInstalled) && oldValue == DetectionTypes.Version)
                )
                {
                    Base = string.Empty;
                }
                ConfigureType();
            }
        }

        partial void OnRuleTypeChanged(RuleType value)
        {
            ConfigureType();
        }

        public RuleCardViewModel()
        {
            FileBasePathVM.CMDUpdated += (s, e) =>
            {
                SaveRule();
                // FileBasePathVM's internal path text isn't an ObservableProperty on this view model, so
                // typing a File rule's path wouldn't otherwise raise PropertyChanged here - which is what
                // RuleDataTemplate listens on to trigger RuleBuilderViewModel.RulesAmended and persist the change.
                OnPropertyChanged(nameof(FileBasePathVM));
            };
            ConfigureType();
            PropertyChanged += (sender, args) => SaveRule();
        }

        public override void Load(RuleBase rule)
        {
            try
            {
                if (rule is Rule ruleToLoad)
                {
                    _isLoading = true;
                    base.Load(ruleToLoad);
                    RuleType = ruleToLoad.Type;
                    Base = ruleToLoad.Base;
                    FileBasePathVM.VariablePath = ruleToLoad.BasePath != null && ruleToLoad.BasePath.Count > 0
                        ? new ObservableCollection<VariableText>(ruleToLoad.BasePath.Select(v => v.Clone()))
                        : new ObservableCollection<VariableText> { new LiteralText(ruleToLoad.Base ?? string.Empty) };
                    Script = ruleToLoad.Script is TextDocument ? ruleToLoad.Script : new TextDocument();
                    Architecture = ruleToLoad.Architecture;
                    DetectionType = ruleToLoad.DetectionType;
                    Comparator = ruleToLoad.Comparator;
                    ComparatorProperty = ruleToLoad.ComparatorProperty ?? string.Empty;
                    ComparatorValue = ruleToLoad.ComparatorValue ?? string.Empty;
                    Context = ruleToLoad.Context != null ? ruleToLoad.Context : Contexts.System;
                }
            }
            catch
            {
                throw;
            }
            finally
            {
                _isLoading = false;
                ConfigureType();
            }
        }

        public bool IsComplete()
        {
            if (RuleType == RuleType.File)
            {
                if (!FileBasePathVM.HasCommand())
                    return false;
            }
            else if (Base == null)
            {
                return false;
            }
            if (DetectionType == null)
                return false;
            if (RuleType != RuleType.PowerShell && Architecture == null)
                return false;
            if (RuleType == RuleType.PowerShell && Script == null && Base == null)
                return false;
            if (
                RuleType == RuleType.Registry &&
                DetectionType != DetectionTypes.Exists &&
                DetectionType != DetectionTypes.NotExists &&
                ComparatorProperty == null
            )
                return false;
            if (
                (
                    DetectionType == DetectionTypes.Binary ||
                    DetectionType == DetectionTypes.Boolean ||
                    DetectionType == DetectionTypes.FileVersion ||
                    DetectionType == DetectionTypes.Number ||
                    DetectionType == DetectionTypes.ProductVersion ||
                    DetectionType == DetectionTypes.String ||
                    DetectionType == DetectionTypes.Version
                ) && (Comparator == null || ComparatorValue == null)
            )
                return false;
            return true;
        }

        public void SaveRule()
        {
            if (!_isLoading && LinkedRule is Rule rule)
            {
                rule.Type = RuleType;
                if (RuleType == RuleType.File)
                {
                    rule.BasePath = FileBasePathVM.VariablePath.ToList();
                    rule.Base = null;
                }
                else
                {
                    rule.Base = Base;
                    rule.BasePath = null;
                }
                rule.Script = Script;
                rule.Architecture = Architecture;
                rule.DetectionType = DetectionType;
                rule.Comparator = Comparator;
                rule.ComparatorProperty = ComparatorProperty;
                rule.ComparatorValue = ComparatorValue;
                rule.Context = Context;
            }
        }

        private void ConfigureType()
        {
            if (_configuringCard) { return; }
            try
            {
                _configuringCard = true;
                int gridColumn = 0;
                CardInnerView.Children.Clear();
                // Base
                Grid baseGrid = new Grid { Tag = "Base" };
                CardInnerView.Children.Add(baseGrid);
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                // Base Type icon
                Uri logoUri = new Uri("avares://SuiteCreatorAvalonia/Assets/Images/FolderIcon.png"); ;
                switch (RuleType)
                {
                    case RuleType.Registry:
                        logoUri = new Uri("avares://SuiteCreatorAvalonia/Assets/Images/RegEditIcon.png");
                        break;
                    case RuleType.File:
                        logoUri = new Uri("avares://SuiteCreatorAvalonia/Assets/Images/FolderIcon.png");
                        break;
                    case RuleType.PowerShell:
                        logoUri = new Uri("avares://SuiteCreatorAvalonia/Assets/Images/PowerShell5Logo.png");
                        break;
                    case RuleType.MSI:
                        logoUri = new Uri("avares://SuiteCreatorAvalonia/Assets/Images/MSILogo.png");
                        break;
                    case RuleType.MSIx:
                        logoUri = new Uri("avares://SuiteCreatorAvalonia/Assets/Images/MSIxLogo.png");
                        break;
                }
                using (Stream stream = AssetLoader.Open(logoUri))
                {
                    Image baseTypeIcon = new Avalonia.Controls.Image
                    {
                        Source = new Bitmap(stream),
                        Width = 25,
                        Height = 25,
                        Margin = new Thickness(0, 0, 5, 0)
                    };
                    Grid.SetColumn(baseTypeIcon, gridColumn);
                    gridColumn++;
                    CardInnerView.Children.Add(baseTypeIcon);
                    CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                }

                // Base Type text
                TextBlock typeTextblock = new();
                typeTextblock.FontWeight = Avalonia.Media.FontWeight.Bold;
                typeTextblock.Margin = new Thickness(0, 0, 5, 0);
                typeTextblock.Bind(TextBlock.TextProperty, new Binding("RuleType") { Mode = BindingMode.TwoWay });
                Grid.SetColumn(typeTextblock, gridColumn);
                gridColumn++;
                CardInnerView.Children.Add(typeTextblock);
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));

                if (RuleType == RuleType.MSI || RuleType == RuleType.MSIx)
                {
                    // Context Picker
                    ComboBox contextComboBox = new();
                    contextComboBox.FontWeight = Avalonia.Media.FontWeight.Bold;
                    contextComboBox.Margin = new Thickness(0, 0, 5, 0);
                    contextComboBox.VerticalAlignment = VerticalAlignment.Center;
                    contextComboBox.ItemsSource = Enum.GetValues(typeof(Contexts)).Cast<Contexts>();
                    contextComboBox.Bind(ComboBox.SelectedItemProperty, new Binding("Context") { Mode = BindingMode.TwoWay });
                    Grid.SetColumn(contextComboBox, gridColumn);
                    gridColumn++;
                    CardInnerView.Children.Add(contextComboBox);
                    CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                }

                if (RuleType != RuleType.PowerShell)
                {
                    // Main TextBox (File rules use the rich variable-aware path control instead)
                    TextBox? ruleMainTextbox = null;
                    Control ruleMainControl;
                    if (RuleType == RuleType.File)
                    {
                        // PathVarsTextBoxView's internal Grid has a "*" column, which cannot be measured
                        // under this Grid's Auto column (infinite available width) without an explicit Width.
                        ruleMainControl = new PathVarsTextBoxView
                        {
                            DataContext = FileBasePathVM,
                            Width = 350,
                            VerticalAlignment = VerticalAlignment.Center
                        };
                    }
                    else
                    {
                        ruleMainTextbox = new TextBox { MinWidth = 250 };
                        ruleMainTextbox.Bind(TextBox.TextProperty, new Binding("Base") { Mode = BindingMode.TwoWay });
                        ruleMainControl = ruleMainTextbox;
                    }
                    Grid.SetColumn(ruleMainControl, gridColumn);
                    gridColumn++;
                    CardInnerView.Children.Add(ruleMainControl);
                    CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    // Architecture Button
                    Button architectureButton = new();
                    architectureButton.Classes.Add("IconButton");
                    architectureButton.Bind(Button.TagProperty, new Binding("Architecture") { Mode = BindingMode.TwoWay, Converter = new ArchitectureToMaterialIconConverter() });
                    architectureButton.Background = null;
                    architectureButton.BorderThickness = new Thickness(0);
                    architectureButton.Click += ChangeBitness;
                    Grid.SetColumn(architectureButton, gridColumn);
                    gridColumn++;
                    CardInnerView.Children.Add(architectureButton);
                    CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    if (Architecture == null)
                        Architecture = Enums.Architecture.x86;
                    // Browse Button
                    Button browseButton = new Button();
                    browseButton.Classes.Add("IconButton");
                    Grid.SetColumn(browseButton, gridColumn);
                    gridColumn++;
                    CardInnerView.Children.Add(browseButton);
                    CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    switch (RuleType)
                    {
                        case RuleType.File:
                            FileBasePathVM.PlaceholderText = "Type or Browse for a file, or use { to insert a variable";
                            browseButton.Tag = "File";
                            browseButton.Click += FileBrowse;
                            ToolTip.SetTip(browseButton, "Browse for a file");
                            break;
                        case RuleType.Registry:
                            ruleMainTextbox!.PlaceholderText = "Type or paste a Registry Key";
                            browseButton.Tag = "CubeUnfolded";
                            browseButton.Click += RegistryOpen;
                            ToolTip.SetTip(browseButton, "Open Registry Editor");
                            break;
                        case RuleType.MSI:
                            switch (DetectionType)
                            {
                                case DetectionTypes.Installed:
                                case DetectionTypes.NotInstalled:
                                    ruleMainTextbox!.PlaceholderText = "Product Code";
                                    browseButton.Tag = "CodeBraces";
                                    browseButton.Click += GetMSIProductCode;
                                    ToolTip.SetTip(browseButton, "Get Product code from an MSI");
                                    break;
                                case DetectionTypes.Version:
                                default:
                                    ruleMainTextbox!.PlaceholderText = "Upgrade Code";
                                    browseButton.Tag = "CodeBraces";
                                    browseButton.Click += GetMSIUpgradeCode;
                                    ToolTip.SetTip(browseButton, "Get Upgrade code from an MSI");
                                    break;
                            }
                            break;
                        case RuleType.MSIx:
                            switch (DetectionType)
                            {
                                case DetectionTypes.Installed:
                                case DetectionTypes.NotInstalled:
                                    ruleMainTextbox!.PlaceholderText = "Package Full Name";
                                    browseButton.Tag = "TagText";
                                    browseButton.Click += GetMSIxPackageFullName;
                                    ToolTip.SetTip(browseButton, "Get Package Full Name from an MSIx");
                                    break;
                                case DetectionTypes.Version:
                                default:
                                    ruleMainTextbox!.PlaceholderText = "Package Family Name";
                                    browseButton.Tag = "HumanMaleFemaleChild";
                                    browseButton.Click += GetMSIxPackageFamilyName;
                                    ToolTip.SetTip(browseButton, "Get Package Family Name from an MSIx");
                                    break;
                            }
                            break;
                        default:
                            throw new NotImplementedException($"The rule type: {RuleType}, is not recognised");
                    }
                }
                if (RuleType == RuleType.PowerShell)
                {
                    // Main Show script editor window button
                    Button showScriptButton = new Button
                    {
                        FontWeight = Avalonia.Media.FontWeight.Bold,
                        Tag = "Powershell"
                    };
                    ToolTip.SetTip(showScriptButton, "Manage Script");
                    showScriptButton.Classes.Add("IconButton");
                    showScriptButton.Background = null;
                    showScriptButton.BorderThickness = new Thickness(0);
                    showScriptButton.Command = new RelayCommand(OpenPSModalWindow);
                    Grid.SetColumn(showScriptButton, gridColumn);
                    gridColumn++;
                    CardInnerView.Children.Add(showScriptButton);
                    CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                }
                // Detection Type
                ComboBox addDetectionTypeComboBox = new ComboBox();
                addDetectionTypeComboBox.VerticalAlignment = VerticalAlignment.Center;
                List<DetectionTypes> applicableDectTypes = ComparatorMapping.RuleTypeToDetection[RuleType];
                addDetectionTypeComboBox.ItemsSource = applicableDectTypes;
                if (!applicableDectTypes.Contains(DetectionType))
                {
                    DetectionType = applicableDectTypes.First();
                }
                addDetectionTypeComboBox.Bind(ComboBox.SelectedItemProperty, new Binding("DetectionType") { Mode = BindingMode.TwoWay });
                Grid.SetColumn(addDetectionTypeComboBox, gridColumn);
                gridColumn++;
                CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                CardInnerView.Children.Add(addDetectionTypeComboBox);

                // Comparator Property
                if (RuleType == RuleType.Registry && DetectionType != DetectionTypes.Exists && DetectionType != DetectionTypes.NotExists)
                {
                    TextBox addComparatorPropTextBox = new TextBox();
                    addComparatorPropTextBox.PlaceholderText = "Reg Property";
                    addComparatorPropTextBox.Bind(TextBox.TextProperty, new Binding("ComparatorProperty") { Mode = BindingMode.TwoWay });
                    Grid.SetColumn(addComparatorPropTextBox, gridColumn);
                    gridColumn++;
                    CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    CardInnerView.Children.Add(addComparatorPropTextBox);
                }

                // Comparator
                if (
                    DetectionType != DetectionTypes.Exists &&
                    DetectionType != DetectionTypes.NotExists &&
                    DetectionType != DetectionTypes.NotInstalled &&
                    DetectionType != DetectionTypes.Installed &&
                    ComparatorMapping.DetectionToComparator.TryGetValue((DetectionTypes)DetectionType, out List<ComparatorPlus>? applicableComparators))
                {
                    if (applicableComparators != null && applicableComparators.Count > 0)
                    {
                        ComboBox comparatorComboBox = new ComboBox();
                        comparatorComboBox.Background = Brushes.Transparent;
                        comparatorComboBox.Padding = new Thickness(0);
                        List<MaterialIconKind> iconKindsList = new List<MaterialIconKind>();
                        foreach (ComparatorPlus c in applicableComparators)
                        {
                            if (ComparatorMapping.ComparatorToIconKind.TryGetValue(c, out MaterialIconKind materialIconKind))
                            {
                                iconKindsList.Add(materialIconKind);
                            }
                        }
                        if (Comparator == null || !applicableComparators.Contains(Comparator.Value))
                        {
                            if (applicableComparators.Contains(ComparatorPlus.GreaterThanOrEqual))
                            {
                                Comparator = ComparatorPlus.GreaterThanOrEqual;
                            }
                            else
                            {
                                Comparator = applicableComparators.First();
                            }
                        }
                        comparatorComboBox.ItemsSource = iconKindsList;
                        comparatorComboBox.Margin = new Thickness(5, 0, 5, 0);
                        comparatorComboBox.VerticalAlignment = VerticalAlignment.Center;
                        comparatorComboBox.Bind(ComboBox.SelectedItemProperty, new Binding("Comparator")
                        {
                            Mode = BindingMode.TwoWay,
                            Converter = new ComparatorPlusToMaterialIconKindConverter()
                        });
                        comparatorComboBox.ItemTemplate = new FuncDataTemplate<MaterialIconKind>((iconKind, _) =>
                        {
                            Border border = new Border();
                            border.Background = Brushes.Transparent;
                            border.Bind(ToolTip.TipProperty, new Binding { Source = iconKind, Converter = new MaterialIconKindToComparatorPlusStringConverter() });
                            MaterialIcon icon = new MaterialIcon { Width = 25, Height = 25 };
                            border.Child = icon;
                            icon.Bind(MaterialIcon.KindProperty, new Binding { Source = iconKind });
                            return border;
                        });
                        Grid.SetColumn(comparatorComboBox, gridColumn);
                        gridColumn++;
                        CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                        CardInnerView.Children.Add(comparatorComboBox);
                    }
                }
                else
                {
                    Comparator = null;
                }
                // Comparator Value
                if (Comparator != null)
                {
                    TextBox addComparatorValueTextBox = new TextBox();
                    addComparatorValueTextBox.PlaceholderText = "Value to compare";
                    addComparatorValueTextBox.Bind(TextBox.TextProperty, new Binding("ComparatorValue") { Mode = BindingMode.TwoWay });
                    Grid.SetColumn(addComparatorValueTextBox, gridColumn);
                    gridColumn++;
                    CardInnerView.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
                    CardInnerView.Children.Add(addComparatorValueTextBox);
                }
            }
            catch
            {
                throw;
            }
            finally
            {
                _configuringCard = false;
            }
        }

        private async void OpenPSModalWindow()
        {
            Window psWindow = new PowerShellWindow();
            PowerShellWindowViewModel vm = new PowerShellWindowViewModel { IsRuleMode = true };
            if (string.IsNullOrWhiteSpace(Base))
            {
                vm.ScriptDoc = Script;
            }
            else
            {
                vm.FilePath = Base;
            }
            vm.Context = Context;
            Window? parent = TopLevel.GetTopLevel(CardInnerView) as Window;
            if (parent == null)
            {
                throw new InvalidOperationException("Could not find parent window to open a model PowerShellWindow");
            }
            psWindow.Height = parent.Height * 0.8;
            psWindow.Width = parent.Width * 0.8;
            psWindow.DataContext = vm;

            await psWindow.ShowDialog(parent);
            Context = vm.Context;
            Base = vm.FilePath;
            if (string.IsNullOrWhiteSpace(Base))
            {
                Script = vm.ScriptDoc;
            }
            else
            {
                Script = new TextDocument();
            }
        }

        private void RegistryOpen(object? sender, RoutedEventArgs e)
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.UseShellExecute = true;
                startInfo.FileName = System.Environment.GetEnvironmentVariable("windir") + "\\Regedit.exe";
                Process.Start(startInfo);
            }
            catch
            {
                //Do nothing, we dont really care if it fails, the user can just open regedit manually to investigate
            }
        }

        private async void GetMSIProductCode(object? sender, RoutedEventArgs e)
        {
            await SetMSICode("ProductCode");
        }

        private async void GetMSIUpgradeCode(object? sender, RoutedEventArgs e)
        {
            await SetMSICode("UpgradeCode");
        }

        private async Task SetMSICode(string codeType)
        {
            var files = await this.OpenFileDialogAsync(new FilePickerOpenOptions
            {
                Title = "Open MSI",
                AllowMultiple = false,
                FileTypeFilter = SysIOPickerTypes.MSI
            });
            if (null != files && files.Any() && files.First() is string filePath && !string.IsNullOrWhiteSpace(filePath))
            {
                try
                {
                    Hashtable msiProps = MSITools.GetMSIProperties(filePath);
                    Base = msiProps[codeType] as string;
                    MSITools.MSISummaryInfo sumInfo = MSITools.GetMSISummaryInfoFromMSI(filePath);
                    if (sumInfo.Template != null && sumInfo.Template.Contains("x64"))
                    {
                        Architecture = Enums.Architecture.x64;
                    }
                    else
                    {
                        Architecture = Enums.Architecture.x86;
                    }
                }
                catch (Exception ex)
                {
                    AppLog.Error($"Failed to obtain MSI {codeType} or Architecture from: {filePath}", ex, "RuleBuilder");
                    ShowErrorPopup($"Failed to obtain MSI {codeType} or Architecture, error: {ex.Message}.");
                }
            }
        }

        private void ShowErrorPopup(string message)
        {
            Flyout errorFlyout = new();
            errorFlyout.Placement = PlacementMode.Bottom;
            errorFlyout.FlyoutPresenterClasses.Add("Warning");
            TextBlock textBlock = new()
            {
                Margin = new Thickness(7),
                Text = message,
                FontWeight = FontWeight.Bold,
                Foreground = Brushes.White,
                TextWrapping = TextWrapping.Wrap,
                MaxWidth = 300,
            };
            errorFlyout.Content = textBlock;
            errorFlyout.ShowAt(CardInnerView);
        }

        private async void GetMSIxPackageFullName(object? sender, RoutedEventArgs e)
        {
            var files = await this.OpenFileDialogAsync(new FilePickerOpenOptions
            {
                Title = "Open MSIx",
                AllowMultiple = false,
                FileTypeFilter = SysIOPickerTypes.MSIx
            });
            if (null != files && files.Any() && !string.IsNullOrEmpty(files.First()))
            {
                using (MSIx msix = new MSIx(files.First()))
                {
                    Base = msix.PackageFullName;
                    Architecture = msix.Architecture.Contains("x64", StringComparison.OrdinalIgnoreCase) ? Enums.Architecture.x64 : Enums.Architecture.x86;
                }
            }
        }

        private async void GetMSIxPackageFamilyName(object? sender, RoutedEventArgs e)
        {
            var files = await this.OpenFileDialogAsync(new FilePickerOpenOptions
            {
                Title = "Open MSIx",
                AllowMultiple = false,
                FileTypeFilter = SysIOPickerTypes.MSIx
            });
            if (null != files && files.Any() && !string.IsNullOrEmpty(files.First()))
            {
                using (MSIx msix = new MSIx(files.First()))
                {
                    Base = msix.PackageFamilyName;
                    Architecture = msix.Architecture.Contains("x64", StringComparison.OrdinalIgnoreCase) ? Enums.Architecture.x64 : Enums.Architecture.x86;
                }
            }
        }

        private void MSILostfocus(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrEmpty(Base)) { return; }
            if (!Regex.IsMatch(Base, "^{"))
            {
                Base = "{" + Base;
            }
            if (!Regex.IsMatch(Base, "}$"))
            {
                Base = Base + "}";
            }
        }

        private void ChangeBitness(object? sender, RoutedEventArgs e)
        {
            if (Architecture == Enums.Architecture.x86)
            {
                Architecture = Enums.Architecture.x64;
            }
            else if (Architecture == Enums.Architecture.x64)
            {
                Architecture = Enums.Architecture.x86;
            }
            else
            {
                Architecture = Enums.Architecture.x86;
            }
        }

        private async void FileBrowse(object? sender, RoutedEventArgs e)
        {
            var files = await this.OpenFileDialogAsync(new FilePickerOpenOptions
            {
                Title = "Open File",
                AllowMultiple = false
            });
            if (null != files && files.Any() && !string.IsNullOrEmpty(files.First()))
            {
                FileBasePathVM.VariablePath = new ObservableCollection<VariableText> { new LiteralText(files.First()) };
            }
        }
    }
}
