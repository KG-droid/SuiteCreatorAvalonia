using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Common;
using System;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class FilesViewModel : ViewModelBase
    {
        [ObservableProperty]
        private IOEventViewModel _ioEventViewModel;

        // Parameterless constructor for design-time support
        public FilesViewModel() : this(new ViewFactory(
            type =>
            {
                return (ViewModelBase)Activator.CreateInstance(type)!;
            })
        )
        {
        }
        public FilesViewModel(ViewFactory fact)
        {
            _ioEventViewModel = (IOEventViewModel)fact.GetVM(typeof(IOEventViewModel));
            _ioEventViewModel.SuiteProp = SuiteProperty.FileEvents;
        }
    }
}
