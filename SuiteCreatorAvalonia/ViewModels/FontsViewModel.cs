using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Factories;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.ViewModels.EventCards;
using System;

namespace SuiteCreatorAvalonia.ViewModels
{
    internal partial class FontsViewModel : ViewModelBase
    {
        [ObservableProperty]
        private IOEventViewModel _ioEventViewModel;

        // Parameterless constructor for design-time support
        public FontsViewModel() : this(new ViewFactory(
            type =>
            {
                return (ViewModelBase)Activator.CreateInstance(type)!;
            })
        )
        {
        }
        public FontsViewModel(ViewFactory fact)
        {
            _ioEventViewModel = (IOEventViewModel)fact.GetVM(typeof(IOEventViewModel));
            _ioEventViewModel.Title = "Fonts";
            _ioEventViewModel.CardVMType = typeof(FontCardViewModel);
            _ioEventViewModel.SuiteProp = SuiteProperty.FontEvents;
        }
    }
}
