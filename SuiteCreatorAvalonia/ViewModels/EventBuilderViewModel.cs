using CommunityToolkit.Mvvm.ComponentModel;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class EventBuilderViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string _eventName = string.Empty;
    }
}
