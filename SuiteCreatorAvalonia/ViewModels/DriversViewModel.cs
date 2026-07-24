using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.ViewModels.EventCards;
using System;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class DriversViewModel : ViewModelBase
    {
        [ObservableProperty]
        private IOEventViewModel _ioEventViewModel;

        // Parameterless constructor for design-time support
        public DriversViewModel() : this(new ViewFactory(
            type =>
            {
                return (ViewModelBase)Activator.CreateInstance(type)!;
            })
        )
        {
        }
        public DriversViewModel(ViewFactory fact)
        {
            _ioEventViewModel = (IOEventViewModel)fact.GetVM(typeof(IOEventViewModel));
            _ioEventViewModel.Title = "Drivers";
            _ioEventViewModel.CardVMType = typeof(DriverCardViewModel);
            _ioEventViewModel.SuiteProp = SuiteProperty.DriverEvents;
        }
    }
}
