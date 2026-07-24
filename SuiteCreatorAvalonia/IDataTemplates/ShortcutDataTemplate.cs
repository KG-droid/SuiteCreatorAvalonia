using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.ViewModels;
using SuiteCreatorAvalonia.Views;
using System;

namespace SuiteCreatorAvalonia.IDataTemplates
{
    internal class ShortcutDataTemplate : IDataTemplate
    {
        private SuiteCoreManager _suiteCoreManager;

        public ShortcutDataTemplate() : this(new SuiteCoreManager())
        {
        }

        public ShortcutDataTemplate(SuiteCoreManager suiteCoreManager)
        {
            _suiteCoreManager = suiteCoreManager;
        }

        public Control Build(object item)
        {
            if (item is Shortcut shortcut)
            {
                ShortcutThumbnailView thumbView = new();
                ShortcutThumbnailViewModel shortThumbVM = new();
                shortThumbVM.PropertyChanged += ShortThumbVM_PropertyChanged;
                shortThumbVM.LoadEvent(shortcut);
                thumbView.DataContext = shortThumbVM;
                return thumbView;
            }
            else
            {
                throw new ArgumentException($"Invalid type for EventCard IDataTemplate, type provide was: {item.GetType().Name}");
            }
        }

        private void ShortThumbVM_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (
                sender is ShortcutThumbnailViewModel sVM &&
                e.PropertyName != nameof(sVM.LinkedShortcutEvent) &&
                e.PropertyName != nameof(sVM.AllIconPreviews) &&
                e.PropertyName != nameof(sVM.SelectedIconPreview) &&
                e.PropertyName != nameof(sVM.ThumbIcon) &&
                e.PropertyName != nameof(sVM.IsMarkedForDeletion) &&
                e.PropertyName != nameof(sVM.IsRemoveMode)
            )
            {
                Shortcut? shortcut = sVM.GetEvent();
                if (shortcut != null)
                    _suiteCoreManager.UpdateShortcutEvent(shortcut);
            }
        }

        public bool Match(object? data)
        {
            return data != null && data is EventCore;
        }
    }
}