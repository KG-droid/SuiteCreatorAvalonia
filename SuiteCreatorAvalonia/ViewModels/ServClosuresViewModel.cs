using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.ViewModels.EventCards;
using System;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class ServClosuresViewModel : ViewModelBase
    {
        [ObservableProperty]
        private IOEventViewModel _ioEventViewModel;

        // Parameterless constructor for design-time support
        public ServClosuresViewModel() : this(new ViewFactory(
            type =>
            {
                return (ViewModelBase)Activator.CreateInstance(type)!;
            })
        )
        {
        }
        public ServClosuresViewModel(ViewFactory fact)
        {
            _ioEventViewModel = (IOEventViewModel)fact.GetVM(typeof(IOEventViewModel));
            _ioEventViewModel.Title = "Service Closures";
            _ioEventViewModel.CardVMType = typeof(ServiceClosureCardViewModel);
            _ioEventViewModel.SuiteProp = SuiteProperty.ServiceClosureEvents;
        }
    }
}
