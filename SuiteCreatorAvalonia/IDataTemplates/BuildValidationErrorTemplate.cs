using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using Material.Icons;
using Material.Icons.Avalonia;
using Microsoft.CodeAnalysis;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.ViewModels;
using SuiteCreatorAvalonia.ViewModels.EventCards;
using SuiteCreatorAvalonia.Views;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace SuiteCreatorAvalonia.IDataTemplates
{
    internal class BuildValidationErrorTemplate : IDataTemplate
    {
        private readonly MainViewModel _mainViewModel;
        private readonly SuiteCoreManager _suiteCoreManager;

        public BuildValidationErrorTemplate(MainViewModel mainViewModel, SuiteCoreManager suiteCoreManager)
        {
            _mainViewModel = mainViewModel;
            _suiteCoreManager = suiteCoreManager;
        }

        public Control Build(object item)
        {
            if (item is SuiteValidationError valErr)
            {
                Button button = new Button()
                {
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 5, 0, 5),
                    Padding = new Thickness(10, 5),
                };
                button.Classes.Add("SuiteValidationErrorButton");
                StackPanel stackPanel = new StackPanel();
                stackPanel.Orientation = Orientation.Horizontal;
                button.Content = stackPanel;
                MaterialIconKind iconKind = MaterialIconKind.Error;
                switch (valErr.ObjectForError)
                {
                    case Models.Rules.RuleSet ruleSet:
                        iconKind = MaterialIconKind.Legal;
                        break;
                    case MSIPkg msi:
                        ToolTip.SetTip(button, "Navigate to this MSI Pkg");
                        iconKind = MaterialIconKind.Package;
                        break;
                    case MSIRemoval msiRem:
                        ToolTip.SetTip(button, "Navigate to this MSI removal Pkg");
                        iconKind = MaterialIconKind.Package;
                        break;
                    case MSIxPkg msixRem:
                        ToolTip.SetTip(button, "Navigate to this MSIx Pkg");
                        iconKind = MaterialIconKind.Package;
                        break;
                    case MSIxRemoval msixRem:
                        ToolTip.SetTip(button, "Navigate to this MSIx removal Pkg");
                        iconKind = MaterialIconKind.Package;
                        break;
                    case OtherPkg other:
                        ToolTip.SetTip(button, "Navigate to this Other Pkg");
                        iconKind = MaterialIconKind.Package;
                        break;
                    case OtherRemovalPkg otherRem:
                        ToolTip.SetTip(button, "Navigate to this OtherRemoval Pkg");
                        iconKind = MaterialIconKind.Package;
                        break;
                    case BrowserExt browserExt:
                        ToolTip.SetTip(button, "Navigate to this Browser Extension event");
                        iconKind = EventTabMappings.Extensions.IconKind;
                        break;
                    case Cert cert:
                        ToolTip.SetTip(button, "Navigate to this Certificate event");
                        iconKind = EventTabMappings.Certs.IconKind;
                        break;
                    case Driver driver:
                        ToolTip.SetTip(button, "Navigate to this Driver event");
                        iconKind = EventTabMappings.Drivers.IconKind;
                        break;
                    case Models.Events.Environment env:
                        ToolTip.SetTip(button, "Navigate to this Environment Variable event");
                        iconKind = EventTabMappings.Environments.IconKind;
                        break;
                    case Executable exec:
                        ToolTip.SetTip(button, "Navigate to this Executable event");
                        iconKind = EventTabMappings.Executables.IconKind;
                        break;
                    case FileSysIO file:
                        ToolTip.SetTip(button, "Navigate to this File event");
                        iconKind = EventTabMappings.Files.IconKind;
                        break;
                    case PowerShell ps:
                        ToolTip.SetTip(button, "Navigate to this PowerShell event");
                        iconKind = EventTabMappings.PSScripts.IconKind;
                        break;
                    case ProcessClosure procClosure:
                        ToolTip.SetTip(button, "Navigate to this Process Closure event");
                        iconKind = EventTabMappings.ProcessClosures.IconKind;
                        break;
                    case Registry reg:
                        ToolTip.SetTip(button, "Navigate to this Registry event");
                        iconKind = EventTabMappings.Registry.IconKind;
                        break;
                    case ServiceClosure servClosure:
                        ToolTip.SetTip(button, "Navigate to this Service Closure event");
                        iconKind = EventTabMappings.ServiceClosures.IconKind;
                        break;
                    case Shortcut shortcut:
                        ToolTip.SetTip(button, "Navigate to this Shortcut event");
                        iconKind = EventTabMappings.Shortcuts.IconKind;
                        break;
                    case Build build:
                        ToolTip.SetTip(button, "Navigate to the Build page");
                        iconKind = EventTabMappings.Build.IconKind;
                        break;
                    default:
                        throw new ArgumentException($"Invalid type for SuiteValidationError ObjectForError, type provide was: {valErr.ObjectForError.GetType().Name}");
                }
                stackPanel.Children.Add(new MaterialIcon { Kind = iconKind });
                stackPanel.Children.Add(
                    new TextBlock()
                    {
                        Text = valErr.Message,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(5, 0, 0, 0)
                    }
                );
                Type? contentType = EventTabMappings.GetEventTabByModelType(valErr.ObjectForError.GetType())?.ContentType;
                if (contentType == null)
                {
                    contentType = typeof(BuildViewModel);
                }

                // For RuleSets, resolve the best navigation target based on what the RuleSet is linked to
                Type ruleSetResolvedContentType = contentType;
                PackageBase? ruleSetLinkedPackage = null;
                EventCore? ruleSetLinkedEvent = null;
                if (valErr.ObjectForError is Models.Rules.RuleSet ruleSetForNav)
                {
                    (List<PackageBase> linkedPackages, List<EventCore> linkedEvents) = _suiteCoreManager.GetRuleSetLinks(ruleSetForNav.Id);
                    if (linkedPackages.Count == 1 && linkedEvents.Count == 0)
                    {
                        ruleSetLinkedPackage = linkedPackages[0];
                        ruleSetResolvedContentType = typeof(PackageViewModel);
                        ToolTip.SetTip(button, $"Navigate to the Package linked to this RuleSet");
                    }
                    else if (linkedPackages.Count == 0 && linkedEvents.Count == 1)
                    {
                        ruleSetLinkedEvent = linkedEvents[0];
                        Type? eventContentType = EventTabMappings.GetEventTabByModelType(linkedEvents[0].GetType())?.ContentType;
                        if (eventContentType != null)
                            ruleSetResolvedContentType = eventContentType;
                        ToolTip.SetTip(button, $"Navigate to the Event linked to this RuleSet");
                    }
                    else
                    {
                        ToolTip.SetTip(button, "Navigate to this RuleSet");
                    }
                }

                button.Command = new RelayCommand(() =>
                {
                    Type navContentType = valErr.ObjectForError is Models.Rules.RuleSet ? ruleSetResolvedContentType : contentType;
                    _mainViewModel.SetView(navContentType);
                    Dispatcher.UIThread.Post(() =>
                    {
                        Type? navType = _mainViewModel.CurrentTabView.GetType();
                        PropertyInfo? eventCardProp = navType.GetProperty("IoEventViewModel");
                        PropertyInfo? pkgProp = navType.GetProperty("PackageList");
                        if (valErr.ObjectForError is Models.Rules.RuleSet)
                        {
                            if (ruleSetLinkedPackage != null && pkgProp != null)
                            {
                                PackageViewModel pkgVM = (PackageViewModel)_mainViewModel.CurrentTabView;
                                pkgVM.SelectedPackage = pkgVM.PackageList.FirstOrDefault(p => p.Id == ruleSetLinkedPackage.Id);
                            }
                            else if (ruleSetLinkedEvent != null && eventCardProp != null)
                            {
                                IOEventViewModel ioVM = (IOEventViewModel)eventCardProp.GetValue(_mainViewModel.CurrentTabView);
                                List<EventCardViewModelBase>? selectedItems = ioVM
                                    .ItemsControl.GetLogicalDescendants().OfType<EventCardView>()
                                    .Where(e => ((EventCardViewModelBase)e.DataContext).IsSelected)
                                    .Cast<EventCardViewModelBase>()
                                    .ToList();
                                if (selectedItems != null && selectedItems.Count > 0)
                                {
                                    foreach (EventCardViewModelBase sel in selectedItems)
                                    {
                                        sel.IsSelected = false;
                                    }
                                }
                                EventCardViewModelBase? cardToSelect = ioVM.ItemsControl
                                    .GetLogicalDescendants()
                                    .OfType<EventCardView>()
                                    .Where(e => ((EventCardViewModelBase)e.DataContext).LinkedEvent.Id == ruleSetLinkedEvent.Id)
                                    .FirstOrDefault()?.DataContext as EventCardViewModelBase;
                                if (cardToSelect != null)
                                {
                                    cardToSelect.IsSelected = true;
                                }
                            }
                            // else: navigated to RulesView, no further selection needed
                        }
                        else if (eventCardProp != null)
                        {
                            // View is event card type
                            IOEventViewModel ioVM = (IOEventViewModel)eventCardProp.GetValue(_mainViewModel.CurrentTabView);
                            List<EventCardViewModelBase>? selectedItems = ioVM
                                .ItemsControl.GetLogicalDescendants().OfType<EventCardView>()
                                .Where(e => ((EventCardViewModelBase)e.DataContext).IsSelected)
                                .Cast<EventCardViewModelBase>()
                                .ToList();
                            if (selectedItems != null && selectedItems.Count > 0)
                            {
                                foreach (EventCardViewModelBase sel in selectedItems)
                                {
                                    sel.IsSelected = false;
                                }
                            }
                            EventCardViewModelBase? cardToSelect = ioVM.ItemsControl
                                .GetLogicalDescendants()
                                .OfType<EventCardView>()
                                .Where(e =>
                                    ((EventCardViewModelBase)e.DataContext).LinkedEvent.Id == ((EventCore)valErr.ObjectForError).Id
                                ).FirstOrDefault()?.DataContext as EventCardViewModelBase;
                            if (cardToSelect != null)
                            {
                                cardToSelect.IsSelected = true;
                            }
                        }
                        else if (pkgProp != null)
                        {
                            // View is package
                            PackageViewModel pkgVM = (PackageViewModel)_mainViewModel.CurrentTabView;
                            pkgVM.SelectedPackage = pkgVM.PackageList.FirstOrDefault(p => p.Id == ((PackageBase)valErr.ObjectForError).Id);
                        }
                    });
                });
                return button;
            }
            else
            {
                throw new ArgumentException($"Invalid type for SuiteValidationError IDataTemplate, type provide was: {item.GetType().Name}");
            }
        }

        public bool Match(object? data)
        {
            return data != null && data is SuiteValidationError;
        }
    }
}