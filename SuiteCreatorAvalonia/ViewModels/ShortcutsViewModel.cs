using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.VisualTree;
using AvaloniaEdit.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.IDataTemplates;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.Views;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class ShortcutsViewModel : ViewModelBase
    {
        private SuiteCoreManager _suiteCoreManager;

        [ObservableProperty]
        public bool _isRemovesEnabled = new();

        [ObservableProperty]
        public ItemsControl _shortsItemsCtrl = new();

        [ObservableProperty]
        public ObservableCollection<Shortcut> _shortcuts = new();

        // parameterless constructor for design-time data
        public ShortcutsViewModel() : this(new SuiteCoreManager())
        {

        }

        public ShortcutsViewModel(SuiteCoreManager suiteCoreManager)
        {
            _suiteCoreManager = suiteCoreManager;
            ShortsItemsCtrl.DataTemplates.Add(new ShortcutDataTemplate(suiteCoreManager));
            ShortsItemsCtrl.ItemsSource = Shortcuts;
            ShortsItemsCtrl.ItemsPanel = new FuncTemplate<Panel>(() =>
            {
                return new WrapPanel
                {
                    Orientation = Orientation.Horizontal
                };
            });
            Shortcuts.CollectionChanged += (s, e) => IsRemovesEnabled = false;
            LoadShortcuts();
        }

        [RelayCommand]
        private void AddNewShortcut()
        {
            _suiteCoreManager.NewShortcutEvent();
            LoadShortcuts();
        }

        [RelayCommand]
        private void ToggleRemoves()
        {
            var shortcuts = ShortsItemsCtrl.GetVisualDescendants()
                .OfType<ShortcutThumbnailView>();
            foreach (var shortcut in shortcuts)
            {
                if (shortcut.DataContext is ShortcutThumbnailViewModel sVM)
                    sVM.IsRemoveMode = !sVM.IsRemoveMode;
            }
            IsRemovesEnabled = !IsRemovesEnabled;
        }

        [RelayCommand]
        public void RemoveShortcuts(bool removeAll = false)
        {
            var shortcuts = ShortsItemsCtrl.GetVisualDescendants()
                .OfType<ShortcutThumbnailView>();
            List<Shortcut> shortcutsToRemove = new();
            if (removeAll)
            {
                shortcutsToRemove.AddRange(Shortcuts);
            }
            else
            {
                foreach (ShortcutThumbnailView shortcut in shortcuts)
                {
                    if (shortcut.DataContext is ShortcutThumbnailViewModel sVM && sVM.IsMarkedForDeletion)
                        shortcutsToRemove.Add(sVM.LinkedShortcutEvent);
                }
            }
            foreach (Shortcut shortcut in shortcutsToRemove)
            {
                _suiteCoreManager.RemoveShortcutEvent(shortcut);
            }
            LoadShortcuts();
        }

        public void LoadShortcuts()
        {
            Shortcuts.Clear();
            Shortcuts.AddRange(_suiteCoreManager.GetShortcutEvents());
        }
    }
}
