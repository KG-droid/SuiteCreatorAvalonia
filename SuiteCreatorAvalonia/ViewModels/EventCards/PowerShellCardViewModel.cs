using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Layout;
using Avalonia.VisualTree;
using AvaloniaEdit.Document;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Image = Avalonia.Controls.Image;

namespace SuiteCreatorAvalonia.ViewModels.EventCards
{
    internal partial class PowerShellCardViewModel : EventCardViewModelBase
    {
        private bool _isLoading = false;
        private SuiteCoreManager _coreManager;

        [ObservableProperty]
        private string? _scriptName;

        [ObservableProperty]
        private string? _scriptPath;

        [ObservableProperty]
        private string? _scriptArgs;

        [ObservableProperty]
        private TextDocument _scriptDoc = new();

        [ObservableProperty]
        public ObservableCollection<FileSystemNode> _supportingFiles = new();

        [ObservableProperty]
        private Enums.Contexts? _context = Enums.Contexts.System;

        public PowerShellCardViewModel() : this(
            new PowerShell(),
            new SuiteCoreManager())
        {
        }

        public PowerShellCardViewModel(PowerShell psEvent, SuiteCoreManager suiteCoreManager) : base(psEvent, suiteCoreManager)
        {
            _coreManager = suiteCoreManager;
            if (psEvent != null)
                LoadEvent(psEvent);
            CreatePowerShellInnerCard();
            ScriptDoc.Changed += (s, e) => OnPropertyChanged(nameof(ScriptDoc));
        }

        private void CreatePowerShellInnerCard()
        {
            Grid mainGrid = new Grid();
            int currentColumn = 0;

            // Script File or Embedded
            Image psIcon = new()
            {
                Width = 25,
                Height = 25,
                Margin = new Thickness(0, 0, 10, 0),
            };
            mainGrid.Children.Add(psIcon);
            Grid.SetColumn(psIcon, currentColumn);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            currentColumn++;

            TextBlock typeTextblock = new();
            typeTextblock.Margin = new Thickness(0, 0, 5, 0);
            typeTextblock.FontWeight = Avalonia.Media.FontWeight.Bold;
            mainGrid.Children.Add(typeTextblock);
            Grid.SetColumn(typeTextblock, currentColumn);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            currentColumn++;

            // PS Button
            Button editPSBrowseBtn = new Button();
            editPSBrowseBtn.Classes.Add("IconButton");
            editPSBrowseBtn.Tag = MaterialIconKind.Powershell.ToString();
            editPSBrowseBtn.Command = new RelayCommand(OpenPSModalWindow);
            ToolTip.SetTip(editPSBrowseBtn, "Manage PowerShell script");
            Help.Annotate(editPSBrowseBtn, "Manage script", "Opens the script editor: write a script inline, or link an external .ps1 file, add supporting files, and choose which user context it runs as.");
            Grid.SetColumn(editPSBrowseBtn, currentColumn);
            mainGrid.Children.Add(editPSBrowseBtn);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            currentColumn++;

            TextBlock pipeTextBlock = new();
            pipeTextBlock.Text = "|";
            pipeTextBlock.Margin = new Thickness(0, 0, 5, 0);
            pipeTextBlock.FontWeight = Avalonia.Media.FontWeight.ExtraBold;
            mainGrid.Children.Add(pipeTextBlock);
            Grid.SetColumn(pipeTextBlock, currentColumn);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            currentColumn++;

            if (string.IsNullOrWhiteSpace(ScriptPath))
            {
                // Embedded Script Icon
                psIcon.Source = Tools.ImageLoader.Get(new Uri("avares://SuiteCreatorAvalonia/Assets/Images/PowerShell5Logo.png"));
                ToolTip.SetTip(psIcon, "Embedded Script");
                typeTextblock.Text = "Embedded Script:";
            }
            else
            {
                // Script File Icon
                psIcon.Source = Tools.ImageLoader.Get(new Uri("avares://SuiteCreatorAvalonia/Assets/Images/PowerShellFile.png"));
                ToolTip.SetTip(psIcon, "Script File");
                typeTextblock.Text = "Script File:";
            }
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            currentColumn++;

            // Name
            TextBlock nameHeaderTextBlock = new();
            nameHeaderTextBlock.Text = "Friendly Name:";
            nameHeaderTextBlock.Margin = new Thickness(0, 0, 5, 0);
            mainGrid.Children.Add(nameHeaderTextBlock);
            Grid.SetColumn(nameHeaderTextBlock, currentColumn);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            currentColumn++;

            TextBlock nameTextBlock = new();
            nameTextBlock.Bind(TextBlock.TextProperty, new Binding("ScriptName"));
            nameTextBlock.Margin = new Thickness(0, 0, 10, 0);
            nameTextBlock.MinWidth = 150;
            Help.Annotate(nameTextBlock, "Friendly name", "A display name for this script, set in the script editor.");
            mainGrid.Children.Add(nameTextBlock);
            Grid.SetColumn(nameTextBlock, currentColumn);
            mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
            currentColumn++;

            if (!string.IsNullOrWhiteSpace(ScriptPath))
            {
                // Script File Name
                TextBlock scriptNameHeaderTextBlock = new();
                scriptNameHeaderTextBlock.Text = "Script File:";
                scriptNameHeaderTextBlock.Margin = new Thickness(0, 0, 5, 0);
                mainGrid.Children.Add(scriptNameHeaderTextBlock);
                Grid.SetColumn(scriptNameHeaderTextBlock, currentColumn);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                currentColumn++;

                TextBlock scriptFileNameTextBlock = new();
                scriptFileNameTextBlock.HorizontalAlignment = HorizontalAlignment.Left;
                scriptFileNameTextBlock.Bind(TextBlock.TextProperty, new Binding("ScriptPath"));
                scriptFileNameTextBlock.Margin = new Thickness(0, 0, 10, 0);
                mainGrid.Children.Add(scriptFileNameTextBlock);
                Grid.SetColumn(scriptFileNameTextBlock, currentColumn);
                mainGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Auto));
                currentColumn++;
            }
            else
            {

            }

            CardInnerView = mainGrid;
        }

        private async void OpenPSModalWindow()
        {
            Window psWindow = new PowerShellWindow();
            PowerShellWindowViewModel vm = new PowerShellWindowViewModel();
            vm.FilePath = ScriptPath;
            if (string.IsNullOrWhiteSpace(ScriptPath))
            {
                vm.ScriptDoc = ScriptDoc;
            }
            foreach (var file in SupportingFiles)
            {
                vm.SupportingFiles.Add(file);
            }
            vm.ScriptName = ScriptName;
            vm.ScriptArgs = ScriptArgs;
            vm.Context = Context;
            Window parent = TopLevel.GetTopLevel(CardInnerView) as Window;
            psWindow.Height = parent.Height * 0.8;
            psWindow.Width = parent.Width * 0.8;
            psWindow.DataContext = vm;
            await psWindow.ShowDialog<TextDocument>(parent);
            ScriptName = vm.ScriptName;
            ScriptArgs = vm.ScriptArgs;
            Context = vm.Context;
            ScriptPath = vm.FilePath;
            if (string.IsNullOrWhiteSpace(ScriptPath))
            {
                ScriptDoc = vm.ScriptDoc;
            }
            else
            {
                ScriptDoc = new TextDocument();
            }
            if (null != vm.SupportingFiles && vm.SupportingFiles.Any())
            {
                SupportingFiles = vm.SupportingFiles;
            }
            CreatePowerShellInnerCard();
        }

        public override void LoadEvent(EventCore eventCore)
        {
            if (eventCore is PowerShell ps)
            {
                _isLoading = true;
                ScriptName = ps.ScriptName;
                ScriptPath = ps.ScriptPath;
                ScriptArgs = ps.ScriptArgs;
                ScriptDoc = ps.ScriptDoc;
                SupportingFiles = ps.SupportingFiles;
                Context = ps.Context;
                Schedules.Clear();
                Schedules.AddRange(ps.Schedules);
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
                LinkedEvent = ps;
                _isLoading = false;
            }
        }

        public override void SaveEvent()
        {
            if (_isLoading) return;
            if (LinkedEvent is PowerShell ps)
            {
                ps.ScriptName = ScriptName;
                ps.ScriptPath = ScriptPath;
                ps.ScriptArgs = ScriptArgs;
                ps.ScriptDoc = ScriptDoc;
                ps.SupportingFiles = SupportingFiles;
                ps.Context = Context;
                ps.Schedules = Schedules.ToList();
                if (!Design.IsDesignMode)
                    _coreManager.UpdatePowerShellEvent(ps);
            }
        }
    }
}
