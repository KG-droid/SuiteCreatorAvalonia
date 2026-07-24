using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.ViewModels;
using System;

namespace SuiteCreatorAvalonia.Factories
{
    public class ViewFactory : ObservableObject
    {
        private Func<Type, ViewModelBase> _tabFac;
        public ViewFactory(Func<Type, ViewModelBase> TabFac)
        {
            _tabFac = TabFac;
        }
        public ViewModelBase GetVM(Type type)
        {
            return _tabFac.Invoke(type);
        }
        public EventTabMappings EventTabs { get; set; } = new EventTabMappings();
    }
}
