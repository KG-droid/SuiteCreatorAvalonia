using CommunityToolkit.Mvvm.ComponentModel;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class ProgressDiagViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string? _message;
        // public ICommand CancelCommand { get; set; } // Optional

        public ProgressDiagViewModel()
        {
        }
    }
}