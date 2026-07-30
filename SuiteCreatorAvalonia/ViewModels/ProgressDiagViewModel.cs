using CommunityToolkit.Mvvm.ComponentModel;
using System.Windows.Input;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class ProgressDiagViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string? _message;

        [ObservableProperty]
        private double? _percentComplete;

        public ICommand? CancelCommand { get; set; }

        public const double ProgressBarWidth = 200;

        public bool IsProgressDetermined => PercentComplete.HasValue;

        public double PercentCompleteValue => PercentComplete ?? 0;

        public double ProgressFillWidth => PercentCompleteValue / 100.0 * ProgressBarWidth;

        public ProgressDiagViewModel()
        {
        }

        partial void OnPercentCompleteChanged(double? value)
        {
            OnPropertyChanged(nameof(IsProgressDetermined));
            OnPropertyChanged(nameof(PercentCompleteValue));
            OnPropertyChanged(nameof(ProgressFillWidth));
        }
    }
}
