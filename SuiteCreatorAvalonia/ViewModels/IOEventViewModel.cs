using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.IDataTemplates;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.ViewModels.EventCards;
using SuiteCreatorAvalonia.Views;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Environment = SuiteCreatorAvalonia.Models.Events.Environment;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class IOEventViewModel : ViewModelBase
    {
        private SuiteCoreManager _suiteCoreMgr;
        private bool _isLoadingFilterOptions = false;
        private List<EventCore> _allEvents = new List<EventCore>();

        [ObservableProperty]
        public SuiteProperty _suiteProp = SuiteProperty.CertEvents;

        [ObservableProperty]
        private string _title = "Example Certificate View";

        [ObservableProperty]
        private Type _cardVMType = typeof(CertCardViewModel);

        [ObservableProperty]
        private List<Sequence>? _sequencesInList;

        [ObservableProperty]
        private List<string>? _stageNamesInList;

        [ObservableProperty]
        private Sequence? _selectedSequenceFilter = null;

        [ObservableProperty]
        private string? _selectedStageNameFilter = null;

        [ObservableProperty]
        private bool _isEventsSelectable = true;

        [ObservableProperty]
        private bool _isRemovesEnabled = false;

        [ObservableProperty]
        private bool _isCloneEnabled = false;

        [ObservableProperty]
        private ItemsControl _itemsControl = new();

        partial void OnSuitePropChanged(SuiteProperty value)
        {
            SetItemsSource();
            UpdateFilterOptions();
        }

        partial void OnSelectedSequenceFilterChanged(Sequence? value)
        {
            if (!_isLoadingFilterOptions)
                FilterEvents();
        }

        partial void OnSelectedStageNameFilterChanged(string? value)
        {
            if (!_isLoadingFilterOptions)
                FilterEvents();
        }

        // Parameterless constructor for design-time support
        public IOEventViewModel() : this(new SuiteCoreManager())
        {

        }

        public IOEventViewModel(SuiteCoreManager coreMgr)
        {
            _suiteCoreMgr = coreMgr;
            _suiteCoreMgr.ProjectChanged += (s, e) => UpdateFilterOptions();
            ItemsControl.DataTemplates.Add(new EventCardDataTemplate(coreMgr));
            SetItemsSource();
            UpdateFilterOptions();
        }

        private void SetItemsSource()
        {
            switch (SuiteProp)
            {
                case SuiteProperty.CertEvents:
                    Title = "Certificate Events";
                    ItemsControl.ItemsSource = _suiteCoreMgr.GetCertEvents();
                    break;
                case SuiteProperty.DriverEvents:
                    Title = "Driver Events";
                    ItemsControl.ItemsSource = _suiteCoreMgr.GetDriverEvents();
                    break;
                case SuiteProperty.EnvironmentEvents:
                    Title = "Environment Events";
                    ItemsControl.ItemsSource = _suiteCoreMgr.GetEnvironmentEvents();
                    break;
                case SuiteProperty.ExecutableEvents:
                    Title = "Executable Events";
                    ItemsControl.ItemsSource = _suiteCoreMgr.GetExecutableEvents();
                    break;
                case SuiteProperty.ExtensionEvents:
                    Title = "Extension Events";
                    ItemsControl.ItemsSource = _suiteCoreMgr.GetExtensionEvents();
                    break;
                case SuiteProperty.FileEvents:
                    Title = "File & Folder Events";
                    ItemsControl.ItemsSource = _suiteCoreMgr.GetFileEvents();
                    break;
                case SuiteProperty.PowerShellEvents:
                    Title = "PowerShell Events";
                    ItemsControl.ItemsSource = _suiteCoreMgr.GetPowerShellEvents();
                    break;
                case SuiteProperty.ProcClosureEvents:
                    Title = "Process Closure Events";
                    ItemsControl.ItemsSource = _suiteCoreMgr.GetProcessClosureEvents();
                    break;
                case SuiteProperty.RegistryEvents:
                    Title = "Registry Events";
                    ItemsControl.ItemsSource = _suiteCoreMgr.GetRegistryEvents();
                    break;
                case SuiteProperty.ServiceClosureEvents:
                    Title = "Service Closure Events";
                    ItemsControl.ItemsSource = _suiteCoreMgr.GetServiceClosureEvents();
                    break;
                case SuiteProperty.ShortcutEvents:
                    Title = "Shortcut Events";
                    ItemsControl.ItemsSource = _suiteCoreMgr.GetShortcutEvents();
                    break;
            }
            if (ItemsControl.ItemsSource.Cast<EventCore>().Any())
                _allEvents = ItemsControl.ItemsSource.Cast<EventCore>().ToList();
            else
                _allEvents = new List<EventCore>();
        }

        private void UpdateFilterOptions()
        {
            _isLoadingFilterOptions = true;
            if (_allEvents.Any())
            {
                SequencesInList = _allEvents
                    .SelectMany(x => x.Schedules.Select(s => s.StageSequence))
                    .Distinct()
                    .ToList();

                StageNamesInList = _allEvents
                    .SelectMany(x => x.Schedules.Select(s => s.EventStage?.Name))
                    .Where(name => !string.IsNullOrEmpty(name))
                    .Distinct()
                    .ToList();
            }
            else
            {
                SequencesInList = null;
                StageNamesInList = null;
            }
            _isLoadingFilterOptions = false;
        }

        private void FilterEvents()
        {
            SetItemsSource();
            if (SelectedSequenceFilter != null || SelectedStageNameFilter != null)
            {
                ItemsControl.ItemsSource = new ObservableCollection<EventCore>(_allEvents.Cast<EventCore>().Where(x =>
                    x.Schedules.Any(s =>
                        (SelectedSequenceFilter == null || s.StageSequence == SelectedSequenceFilter) &&
                              (SelectedStageNameFilter == null || s.EventStage.Name == SelectedStageNameFilter)
                              )
                    ));
            }
        }

        public void SaveOrder()
        {
            // ItemsControl.ItemsSource may only be a filtered subset of _allEvents, so merge
            // the new relative order of the visible items back into the full event list rather
            // than discarding whatever is currently filtered out.
            Queue<Guid> visibleOrder = new(ItemsControl.ItemsSource.Cast<EventCore>().Select(e => e.Id));
            HashSet<Guid> visibleIds = new(visibleOrder);
            List<Guid> mergedOrder = _allEvents
                .Select(e => visibleIds.Contains(e.Id) ? visibleOrder.Dequeue() : e.Id)
                .ToList();
            _suiteCoreMgr.ReorderEvents(SuiteProp, mergedOrder);
        }

        [RelayCommand]
        public async Task AddCard()
        {
            IsRemovesEnabled = false;
            switch (SuiteProp)
            {
                case SuiteProperty.CertEvents:
                    _suiteCoreMgr.NewCertEvent();
                    break;
                case SuiteProperty.DriverEvents:
                    _suiteCoreMgr.NewDriverEvent();
                    break;
                case SuiteProperty.EnvironmentEvents:
                    _suiteCoreMgr.NewEnvironmentEvent();
                    break;
                case SuiteProperty.ExecutableEvents:
                    _suiteCoreMgr.NewExecutableEvent();
                    break;
                case SuiteProperty.ExtensionEvents:
                    _suiteCoreMgr.NewExtensionEvent();
                    break;
                case SuiteProperty.FileEvents:
                    _suiteCoreMgr.NewFileEvent();
                    break;
                case SuiteProperty.PowerShellEvents:
                    PowerShell? result = await OpenPSModalWindow();
                    if (result != null)
                    {
                        PowerShell newEvent = _suiteCoreMgr.NewPowerShellEvent();
                        result.Id = newEvent.Id;
                        result.Schedules = newEvent.Schedules;
                        _suiteCoreMgr.UpdatePowerShellEvent(result);
                    }
                    break;
                case SuiteProperty.ProcClosureEvents:
                    _suiteCoreMgr.NewProcessClosureEvent();
                    break;
                case SuiteProperty.RegistryEvents:
                    _suiteCoreMgr.NewRegistryEvent();
                    break;
                case SuiteProperty.ServiceClosureEvents:
                    _suiteCoreMgr.NewServiceClosureEvent();
                    break;
                case SuiteProperty.ShortcutEvents:
                    _suiteCoreMgr.NewShortcutEvent();
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(SuiteProp), SuiteProp, "Unknown Suite Property type, so can't add a new event");
            }
            SetItemsSource();
        }

        [RelayCommand]
        public void RemoveFilter()
        {
            SelectedSequenceFilter = null;
            SelectedStageNameFilter = null;
        }

        [RelayCommand]
        public void RemoveSequenceFilter()
        {
            SelectedSequenceFilter = null;
        }

        [RelayCommand]
        public void RemoveStageFilter()
        {
            SelectedStageNameFilter = null;
        }

        [RelayCommand]
        public void ToggleRemoves()
        {
            IEnumerable<EventCardView> cards = ItemsControl.GetLogicalDescendants().OfType<EventCardView>();
            foreach (EventCardView card in cards)
            {
                EventCardViewModelBase cardVM = (card.DataContext as EventCardViewModelBase);
                if (IsRemovesEnabled)
                {
                    cardVM.IsRemoveMode = false;
                }
                else
                {
                    cardVM.IsRemoveMode = true;
                    cardVM.IsSelected = false;
                    cardVM.IsCloneMode = false;
                    cardVM.IsMarkedForClone = false;
                }
            }
            IsRemovesEnabled = !IsRemovesEnabled;
            if (IsRemovesEnabled) IsCloneEnabled = false;
            IsEventsSelectable = !IsRemovesEnabled;
        }

        [RelayCommand]
        public void DeleteCards(bool deleteAll = false)
        {
            IEnumerable<EventCardView> cards = ItemsControl.GetLogicalDescendants().OfType<EventCardView>();
            List<EventCore> eventsForRemoval = new();
            if (deleteAll)
            {
                eventsForRemoval = _allEvents;
            }
            else
            {
                foreach (EventCardView card in cards)
                {
                    EventCardViewModelBase cardVM = (card.DataContext as EventCardViewModelBase);
                    if (cardVM.IsMarkedForDeletion)
                    {
                        eventsForRemoval.Add(cardVM.LinkedEvent);
                    }
                }
            }
            switch (SuiteProp)
            {
                case SuiteProperty.CertEvents:
                    foreach (EventCore eventCore in eventsForRemoval)
                    {
                        if (eventCore is Cert cert)
                        {
                            _suiteCoreMgr.RemoveCertEvent(cert);
                        }
                    }
                    break;
                case SuiteProperty.DriverEvents:
                    foreach (EventCore eventCore in eventsForRemoval)
                    {
                        if (eventCore is Driver driver)
                        {
                            _suiteCoreMgr.RemoveDriverEvent(driver);
                        }
                    }
                    break;
                case SuiteProperty.EnvironmentEvents:
                    foreach (EventCore eventCore in eventsForRemoval)
                    {
                        if (eventCore is Environment environment)
                        {
                            _suiteCoreMgr.RemoveEnvironmentEvent(environment);
                        }
                    }
                    break;
                case SuiteProperty.ExecutableEvents:
                    foreach (EventCore eventCore in eventsForRemoval)
                    {
                        if (eventCore is Executable executable)
                        {
                            _suiteCoreMgr.RemoveExecutableEvent(executable);
                        }
                    }
                    break;
                case SuiteProperty.ExtensionEvents:
                    foreach (EventCore eventCore in eventsForRemoval)
                    {
                        if (eventCore is BrowserExt ext)
                        {
                            _suiteCoreMgr.RemoveExtensionEvent(ext);
                        }
                    }
                    break;
                case SuiteProperty.FileEvents:
                    foreach (EventCore eventCore in eventsForRemoval)
                    {
                        if (eventCore is FileSysIO file)
                        {
                            _suiteCoreMgr.RemoveFileEvent(file);
                        }
                    }
                    break;
                case SuiteProperty.PowerShellEvents:
                    foreach (EventCore eventCore in eventsForRemoval)
                    {
                        if (eventCore is PowerShell script)
                        {
                            _suiteCoreMgr.RemovePowerShellEvent(script);
                        }
                    }
                    break;
                case SuiteProperty.ProcClosureEvents:
                    foreach (EventCore eventCore in eventsForRemoval)
                    {
                        if (eventCore is ProcessClosure procClosure)
                        {
                            _suiteCoreMgr.RemoveProcessClosureEvent(procClosure);
                        }
                    }
                    break;
                case SuiteProperty.RegistryEvents:
                    foreach (EventCore eventCore in eventsForRemoval)
                    {
                        if (eventCore is Registry reg)
                        {
                            _suiteCoreMgr.RemoveRegistryEvent(reg);
                        }
                    }
                    break;
                case SuiteProperty.ServiceClosureEvents:
                    foreach (EventCore eventCore in eventsForRemoval)
                    {
                        if (eventCore is ServiceClosure serviceClosure)
                        {
                            _suiteCoreMgr.RemoveServiceClosureEvent(serviceClosure);
                        }
                    }
                    break;
                case SuiteProperty.ShortcutEvents:
                    foreach (EventCore eventCore in eventsForRemoval)
                    {
                        if (eventCore is Shortcut shortcut)
                        {
                            _suiteCoreMgr.RemoveShortcutEvent(shortcut);
                        }
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(SuiteProp), SuiteProp, "Unknown Suite Property type, so can't remove the event");
            }

            SetItemsSource();
            if (!deleteAll) ToggleRemoves();
        }

        [RelayCommand]
        public void ToggleClone()
        {
            IEnumerable<EventCardView> cards = ItemsControl.GetLogicalDescendants().OfType<EventCardView>();
            foreach (EventCardView card in cards)
            {
                EventCardViewModelBase cardVM = (card.DataContext as EventCardViewModelBase);
                if (IsCloneEnabled)
                {
                    cardVM.IsCloneMode = false;
                }
                else
                {
                    cardVM.IsCloneMode = true;
                    cardVM.IsSelected = false;
                    cardVM.IsRemoveMode = false;
                    cardVM.IsMarkedForDeletion = false;
                }
            }
            IsCloneEnabled = !IsCloneEnabled;
            if (IsCloneEnabled) IsRemovesEnabled = false;
            IsEventsSelectable = !IsCloneEnabled;
        }

        [RelayCommand]
        public void CloneCards()
        {
            IEnumerable<EventCardView> cards = ItemsControl.GetLogicalDescendants().OfType<EventCardView>();
            List<EventCore> eventsForClone = new();
            foreach (EventCardView card in cards)
            {
                EventCardViewModelBase cardVM = (card.DataContext as EventCardViewModelBase);
                if (cardVM.IsMarkedForClone)
                {
                    eventsForClone.Add(cardVM.LinkedEvent);
                }
            }
            foreach (EventCore eventCore in eventsForClone)
            {
                _suiteCoreMgr.AddClonedEvent(eventCore);
            }
            SetItemsSource();
            ToggleClone();
        }

        private async Task<PowerShell?> OpenPSModalWindow()
        {
            Window psWindow = new PowerShellWindow();
            PowerShellWindowViewModel pvm = new PowerShellWindowViewModel();
            Window parent = TopLevel.GetTopLevel(ItemsControl) as Window;
            psWindow.Height = parent.Height * 0.8;
            psWindow.Width = parent.Width * 0.8;
            psWindow.DataContext = pvm;
            await psWindow.ShowDialog(parent);
            if (!string.IsNullOrWhiteSpace(pvm.FilePath) || !string.IsNullOrWhiteSpace(pvm.ScriptDoc.Text))
            {
                return new PowerShell()
                {
                    Context = pvm.Context,
                    ScriptDoc = pvm.ScriptDoc,
                    ScriptName = pvm.ScriptName,
                    ScriptPath = pvm.FilePath,
                    SupportingFiles = pvm.SupportingFiles
                };
            }
            else
            {
                return null;
            }
        }
    }
}