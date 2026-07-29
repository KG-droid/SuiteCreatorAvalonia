using CommunityToolkit.Mvvm.ComponentModel;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class ProgressDiagViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string? _message;

        [ObservableProperty]
        private double? _zipPercent;
        // public ICommand CancelCommand { get; set; } // Optional

        public bool IsZipProgressActive => ZipPercent.HasValue;

        public double ZipPercentValue => ZipPercent ?? 0;

        public ProgressDiagViewModel()
        {
        }

        partial void OnZipPercentChanged(double? value)
        {
            OnPropertyChanged(nameof(IsZipProgressActive));
            OnPropertyChanged(nameof(ZipPercentValue));
        }
    }
}