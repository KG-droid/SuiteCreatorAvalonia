using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.ViewModels.EventCards;
using System;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class BrowserExtViewModel : ViewModelBase
    {
        [ObservableProperty]
        private IOEventViewModel _ioEventViewModel;

        // Parameterless constructor for design-time support
        public BrowserExtViewModel() : this(new ViewFactory(
            type =>
            {
                return (ViewModelBase)Activator.CreateInstance(type)!;
            })
        )
        {
        }
        public BrowserExtViewModel(ViewFactory fact)
        {
            _ioEventViewModel = (IOEventViewModel)fact.GetVM(typeof(IOEventViewModel));
            _ioEventViewModel.Title = "Browser Extensions";
            _ioEventViewModel.CardVMType = typeof(BrowserExtCardViewModel);
            _ioEventViewModel.SuiteProp = SuiteProperty.ExtensionEvents;
        }
    }
}
