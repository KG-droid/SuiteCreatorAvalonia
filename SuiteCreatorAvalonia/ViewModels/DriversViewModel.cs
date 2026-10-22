using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.ViewModels.EventCards;
using System;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class DriversViewModel : ViewModelBase
    {
        private const int DriversSectionIndex = 0;
        private const int FontsSectionIndex = 1;

        private readonly IOEventViewModel _driversEventViewModel;
        private readonly IOEventViewModel _fontsEventViewModel;

        public IOEventViewModel DriversEventViewModel => _driversEventViewModel;

        public IOEventViewModel FontsEventViewModel => _fontsEventViewModel;

        // The event list of whichever section (Drivers or Fonts) is currently showing
        [ObservableProperty]
        private IOEventViewModel _ioEventViewModel;

        [ObservableProperty]
        private int _selectedSectionIndex = DriversSectionIndex;

        partial void OnSelectedSectionIndexChanged(int value)
        {
            IoEventViewModel = value == FontsSectionIndex ? _fontsEventViewModel : _driversEventViewModel;
        }

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
            _driversEventViewModel = (IOEventViewModel)fact.GetVM(typeof(IOEventViewModel));
            _driversEventViewModel.Title = "Drivers";
            _driversEventViewModel.CardVMType = typeof(DriverCardViewModel);
            _driversEventViewModel.SuiteProp = SuiteProperty.DriverEvents;

            _fontsEventViewModel = (IOEventViewModel)fact.GetVM(typeof(IOEventViewModel));
            _fontsEventViewModel.Title = "Fonts";
            _fontsEventViewModel.CardVMType = typeof(FontCardViewModel);
            _fontsEventViewModel.SuiteProp = SuiteProperty.FontEvents;

            _ioEventViewModel = _driversEventViewModel;
        }

        /// <summary>Switches to the section (Drivers or Fonts) that holds the given event, so navigating to it lands on the right list.</summary>
        public void ShowSectionFor(EventCore? evt)
        {
            if (evt is SystemFont)
                SelectedSectionIndex = FontsSectionIndex;
            else if (evt is Driver)
                SelectedSectionIndex = DriversSectionIndex;
        }
    }
}
