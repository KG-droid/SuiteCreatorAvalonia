using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogHostAvalonia;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels
{
    /// <summary>Prompts for a new name for an existing RuleSet, e.g. when a new package would otherwise collide with an orphaned rule of the same name.</summary>
    public partial class RenameExistingRuleViewModel : ViewModelBase
    {
        private readonly string _currentName;
        private readonly HashSet<string> _takenNames;

        public string Message { get; }

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ConfirmCommand))]
        private string? _newName;

        [ObservableProperty]
        private string? _errorMessage;

        // Parameterless constructor for design-time support
        public RenameExistingRuleViewModel() : this("Existing Rule", Array.Empty<string>())
        {
        }

        public RenameExistingRuleViewModel(string currentName, IEnumerable<string> takenNames)
        {
            _currentName = currentName;
            _takenNames = new HashSet<string>(takenNames, StringComparer.OrdinalIgnoreCase);
            Message = $"Enter a new name for the existing rule \"{currentName}\", so the new package can use \"{currentName}\" for its own rule.";
            NewName = currentName;
        }

        partial void OnNewNameChanged(string? value)
        {
            string trimmed = value?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(trimmed))
            {
                ErrorMessage = null;
            }
            else if (trimmed.Equals(_currentName, StringComparison.OrdinalIgnoreCase))
            {
                ErrorMessage = "Enter a different name - this is the rule's current name.";
            }
            else if (_takenNames.Contains(trimmed))
            {
                ErrorMessage = $"A rule named \"{trimmed}\" already exists.";
            }
            else
            {
                ErrorMessage = null;
            }
        }

        private bool CanConfirm() =>
            !string.IsNullOrWhiteSpace(NewName) &&
            !NewName.Trim().Equals(_currentName, StringComparison.OrdinalIgnoreCase) &&
            !_takenNames.Contains(NewName.Trim());

        [RelayCommand(CanExecute = nameof(CanConfirm))]
        private void Confirm()
        {
            DialogHost.Close("MainDialogHost", NewName!.Trim());
        }

        [RelayCommand]
        private void Cancel()
        {
            DialogHost.Close("MainDialogHost", null);
        }
    }
}
