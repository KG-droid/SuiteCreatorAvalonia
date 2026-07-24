using CommunityToolkit.Mvvm.ComponentModel;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class MSIRemoveItemViewModel : ViewModelBase
    {
        [ObservableProperty]
        private bool _isProductRemoval = false;

        [ObservableProperty]
        private string? _code;

        [ObservableProperty]
        private string? _logPath;

        [ObservableProperty]
        private string? _additionalParams;

        public MSIRemoveItemViewModel()
        {
            
        }
    }
}