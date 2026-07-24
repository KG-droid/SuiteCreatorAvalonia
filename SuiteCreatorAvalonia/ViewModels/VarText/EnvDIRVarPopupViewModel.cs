using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorModels.Enums;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class EnvDIRVarPopupViewModel : ViewModelBase
    {
        private List<string> _variableNamesAll;

        [ObservableProperty]
        private List<string> _filteredVariableNames;

        [ObservableProperty]
        private string? _selectedItem;

        [ObservableProperty]
        private string _searchTerm;

        public event EventHandler<string> VariableSelected;

        partial void OnSearchTermChanged(string value)
        {
            FilteredVariableNames = _variableNamesAll.Where(folder => folder.StartsWith(value, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public void SelectVariable(string value) // Called via the View
        {
            if (!string.IsNullOrEmpty(value))
            {
                VariableSelected?.Invoke(this, value);
            }
        }

        public EnvDIRVarPopupViewModel(bool dontShowUserVars = false)
        {
            if (dontShowUserVars)
            {
                _variableNamesAll = Enum.GetNames(typeof(SpecialSysFolderVar)).OrderBy(name => name).ToList();
            }
            else
            {
                _variableNamesAll = Enum.GetNames(typeof(SpecialFolderVar)).OrderBy(name => name).ToList();
            }
            FilteredVariableNames = _variableNamesAll;
        }
    }
}
