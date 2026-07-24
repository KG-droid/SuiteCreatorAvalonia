using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Platform.Storage;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Common.TreeNodes;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.Views;
using Avalonia.Threading;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels.EventCards
{
    internal partial class ExecCardViewModel : EventCardViewModelBase
    {
        private bool _isLoading = false;
        private SuiteCoreManager _coreManager;
        private readonly DispatcherTimer _saveDebounceTimer;
        private PathVarsTextBoxViewModel _cmdVM;
        private PathVarsTextBoxViewModel _workingDirVM = new();
        private ToggleButton _workingDirToggleButton = new();

        [ObservableProperty]
        private ObservableCollection<FileSystemNode> _fileTree = new ObservableCollection<FileSystemNode> { new FileSystemNode("EventRoot", new ObservableCollection<FileSystemNode>()) };

        [ObservableProperty]
        private ObservableCollection<VariableText> _workingDIR;

        [ObservableProperty]
        private bool _continueOnError = false;

        [ObservableProperty]
        private bool _continueOnNotFound = false;

        [ObservableProperty]
        private bool _secureParams = false;

        public ExecCardViewModel() : this(
            new Executable(),
            new SuiteCoreManager())
        {
        }

        public ExecCardViewModel(Executable exec, SuiteCoreManager suiteCoreManager) : base(exec, suiteCoreManager)
        {
            _coreManager = suiteCoreManager;
            _saveDebounceTimer = new DispatcherTimer { Interval = System.TimeSpan.FromMilliseconds(200) };
            _saveDebounceTimer.Tick += (s, e) => { _saveDebounceTimer.Stop(); SaveEvent(); };
            _cmdVM = new(FileTree);
            _cmdVM.CMDUpdated += (s, e) =>
            {
                OnPropertyChanged(nameof(LiteralText));
            };
            _workingDirVM.CMDUpdated += (s, e) =>
            {
                OnPropertyChanged(nameof(WorkingDIR));
            };
            CreateExecCardView();
            LoadEvent(exec);
        }

        private void CreateExecCardView()
        {
            Grid mainGrid = new Grid();
            mainGrid.ColumnDefinitions = new ColumnDefinitions("*,Auto");
            mainGrid.Margin = new Thickness(5);

            // Working DIR button flyout
            Flyout workingDirFlyout = new Flyout();
            Grid flyoutGrid = new Grid();
            flyoutGrid.Height = 40;
            flyoutGrid.Width = 450;
            flyoutGrid.Margin = new Thickness(5, 2, 5, 2);
            flyoutGrid.ColumnDefinitions = new ColumnDefinitions("*,Auto");
            workingDirFlyout.Content = flyoutGrid;

            // CMD
            PathVarsTextBoxView cmdView = new PathVarsTextBoxView();
            cmdView.Height = 34;
            cmdView.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            cmdView.DataContext = _cmdVM;
            Help.Annotate(cmdView, "Command", "The executable to run, plus any arguments. Type '{' to insert a suite variable such as a file path or install directory.");
            Grid.SetColumn(cmdView, 0);
            mainGrid.Children.Add(cmdView);

            // Toggle buttons StackPanel
            StackPanel buttonsStackPanel = new StackPanel
            {
                Margin = new Thickness(5, 0, 5, 0),
                VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
                Orientation = Avalonia.Layout.Orientation.Horizontal
            };
            Grid.SetColumn(buttonsStackPanel, 1);
            mainGrid.Children.Add(buttonsStackPanel);

            // WorkingDIR
            _workingDirToggleButton = new ToggleButton();
            _workingDirToggleButton.Classes.Add("IconToggleButton");
            _workingDirToggleButton.Tag = "Briefcase";
            _workingDirToggleButton.Flyout = workingDirFlyout;
            _workingDirToggleButton.Click += (s, e) => WorkDIRToggleCheck();
            ToolTip.SetTip(_workingDirToggleButton, "Set a Working DIR (Defaults to the DIR of the executable otherwise)");
            Help.Annotate(_workingDirToggleButton, "Working directory", "Set a working directory for the executable to run from (defaults to the executable's own directory otherwise).");
            buttonsStackPanel.Children.Add(_workingDirToggleButton);

            PathVarsTextBoxView workingDIRVarsTextView = new PathVarsTextBoxView();
            _workingDirVM.CMDUpdated += (s, e) => WorkDIRToggleCheck();
            workingDIRVarsTextView.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center;
            workingDIRVarsTextView.DataContext = _workingDirVM;
            _workingDirVM.PlaceholderText = "Variables can be added via the { character";
            _workingDirVM.IsAddFileButtonVisible = false;
            Help.Annotate(workingDIRVarsTextView, "Working directory path", "The directory the executable runs from. Type '{' to insert a suite variable, or use the browse button.");
            workingDIRVarsTextView.HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch;
            Grid.SetColumn(workingDIRVarsTextView, 0);
            flyoutGrid.Children.Add(workingDIRVarsTextView);

            Button workingDIRBrowseButton = new Button();
            workingDIRBrowseButton.Classes.Add("IconButton");
            workingDIRBrowseButton.Tag = "FolderPlus";
            workingDIRBrowseButton.Command = new RelayCommand(WorkingDIRPathBrowse);
            ToolTip.SetTip(workingDIRBrowseButton, "Browse for a working DIR");
            Help.Annotate(workingDIRBrowseButton, "Browse", "Browse for a working directory.");
            Grid.SetColumn(workingDIRBrowseButton, 1);
            flyoutGrid.Children.Add(workingDIRBrowseButton);

            // ContinueOnError
            ToggleButton continueOnErrorToggleButton = new ToggleButton();
            continueOnErrorToggleButton.Classes.Add("IconToggleButton");
            continueOnErrorToggleButton.Tag = "Error";
            continueOnErrorToggleButton.Bind(CheckBox.IsCheckedProperty, new Binding("ContinueOnError"));
            ToolTip.SetTip(continueOnErrorToggleButton, "Continue if exe errors");
            Help.Annotate(continueOnErrorToggleButton, "Continue on error", "If enabled, the suite keeps going even if this executable returns an error.");
            buttonsStackPanel.Children.Add(continueOnErrorToggleButton);

            // ContinueOnNotFound
            ToggleButton continueOnNotFoundToggleButton = new ToggleButton();
            continueOnNotFoundToggleButton.Bind(CheckBox.IsCheckedProperty, new Binding("ContinueOnNotFound"));
            continueOnNotFoundToggleButton.Classes.Add("IconToggleButton");
            continueOnNotFoundToggleButton.Tag = "MagnifyClose";
            ToolTip.SetTip(continueOnNotFoundToggleButton, "Continue if exe isnt found");
            Help.Annotate(continueOnNotFoundToggleButton, "Continue if not found", "If enabled, the suite keeps going even if this executable can't be found.");
            buttonsStackPanel.Children.Add(continueOnNotFoundToggleButton);

            // SecureParams
            ToggleButton secureToggleButton = new ToggleButton();
            secureToggleButton.Bind(CheckBox.IsCheckedProperty, new Binding("SecureParams"));
            secureToggleButton.Classes.Add("IconToggleButton");
            secureToggleButton.Tag = "EyeOff";
            ToolTip.SetTip(secureToggleButton, "Hide the arguments from the log file");
            Help.Annotate(secureToggleButton, "Secure parameters", "If enabled, the command's arguments are hidden from the suite's log file — use this for commands containing secrets like passwords.");
            buttonsStackPanel.Children.Add(secureToggleButton);

            CardInnerView = mainGrid;
        }

        private void SubscribeToFileTree(ObservableCollection<FileSystemNode> nodes)
        {
            nodes.CollectionChanged += OnFileTreeCollectionChanged;
            foreach (FileSystemNode node in nodes)
            {
                if (node.SubNodes != null)
                    SubscribeToFileTree(node.SubNodes);
            }
        }

        private void UnsubscribeFromFileTree(ObservableCollection<FileSystemNode> nodes)
        {
            nodes.CollectionChanged -= OnFileTreeCollectionChanged;
            foreach (FileSystemNode node in nodes)
            {
                if (node.SubNodes != null)
                    UnsubscribeFromFileTree(node.SubNodes);
            }
        }

        private void OnFileTreeCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        {
            if (e.NewItems != null)
            {
                foreach (FileSystemNode node in e.NewItems)
                {
                    if (node.SubNodes != null)
                        SubscribeToFileTree(node.SubNodes);
                }
            }
            if (e.OldItems != null)
            {
                foreach (FileSystemNode node in e.OldItems)
                {
                    if (node.SubNodes != null)
                        UnsubscribeFromFileTree(node.SubNodes);
                }
            }
            _saveDebounceTimer.Stop();
            _saveDebounceTimer.Start();
        }

        private void WorkDIRToggleCheck()
        {
            if (_workingDirVM.HasCommand())
                _workingDirToggleButton.IsChecked = true;
            else
                _workingDirToggleButton.IsChecked = false;
        }

        private async void WorkingDIRPathBrowse()
        {
            IEnumerable<string> result = (await this.OpenFolderDialogAsync(new FolderPickerOpenOptions() { AllowMultiple = false, Title = "Browse for a working directory" }));
            if (result != null && result.Any())
            {
                _workingDirVM.VariablePath.Add(
                    new FileVar(
                        new FileSystemNode(result.First())
                        )
                    );
            }
        }

        public override void LoadEvent(EventCore eventCore)
        {
            if (eventCore is Executable env)
            {
                _isLoading = true;
                _cmdVM.VariablePath.Clear();
                _workingDirVM.VariablePath.Clear();
                if (env.Command != null)
                {
                    _cmdVM.VariablePath.AddRange(env.Command);
                }
                if (env.WorkingDIR != null)
                {
                    _workingDirVM.VariablePath.AddRange(env.WorkingDIR);
                }
                UnsubscribeFromFileTree(FileTree);
                FileTree.Clear();
                if (env.TreeNodes != null)
                {
                    FileTree.AddRange(env.TreeNodes);
                }
                SubscribeToFileTree(FileTree);
                ContinueOnError = env.ContinueOnError;
                ContinueOnNotFound = env.ContinueOnNotFound;
                SecureParams = env.SecureParams;
                _workingDirToggleButton.IsChecked = _workingDirVM.HasCommand();
                Schedules.Clear();
                Schedules.AddRange(env.Schedules);
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
                LinkedEvent = env;
                _isLoading = false;
            }
        }

        public override void SaveEvent()
        {
            if (_isLoading) return;
            if (LinkedEvent is Executable exec)
            {
                exec.Command = _cmdVM.VariablePath.ToList();
                exec.WorkingDIR = WorkingDIR != null ? WorkingDIR.ToList() : null;
                exec.TreeNodes = FileTree.ToList();
                exec.ContinueOnError = ContinueOnError;
                exec.ContinueOnNotFound = ContinueOnNotFound;
                exec.SecureParams = SecureParams;
                exec.Schedules = Schedules.ToList();
                if (!Design.IsDesignMode)
                    _coreManager.UpdateExecutableEvent(exec);
            }
        }
    }
}
