using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using Environment = SuiteCreatorAvalonia.Models.Events.Environment;

namespace SuiteCreatorAvalonia.ViewModels.EventCards
{
    internal abstract partial class EventCardWithPermanenceViewModelBase : EventCardViewModelBase
    {
        [ObservableProperty]
        private bool _isPermanent;


        // Paramless constructor with example values for design view
        public EventCardWithPermanenceViewModelBase() : this(
            new Environment(),
            new SuiteCoreManager())
        {
        }

        public EventCardWithPermanenceViewModelBase(EventCore eventCore, SuiteCoreManager suiteCoreManager) : base(eventCore, suiteCoreManager)
        {
        }
    }
}
