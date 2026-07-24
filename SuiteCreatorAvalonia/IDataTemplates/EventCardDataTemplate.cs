using Avalonia.Controls;
using Avalonia.Controls.Templates;
using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Services;
using SuiteCreatorAvalonia.ViewModels.EventCards;
using SuiteCreatorAvalonia.Views;
using System;
using Environment = SuiteCreatorAvalonia.Models.Events.Environment;

namespace SuiteCreatorAvalonia.IDataTemplates
{
    internal class EventCardDataTemplate : IDataTemplate
    {
        private readonly SuiteCoreManager _coreManager;

        public EventCardDataTemplate(SuiteCoreManager suiteCoreManager)
        {
            _coreManager = suiteCoreManager;
        }
        public Control Build(object item)
        {
            if (item is EventCore core)
            {
                EventCardView eventView = new EventCardView();
                switch (core)
                {
                    case BrowserExt:
                        eventView.DataContext = new BrowserExtCardViewModel(core as BrowserExt, _coreManager);
                        break;
                    case Cert:
                        eventView.DataContext = new CertCardViewModel(core as Cert, _coreManager);
                        break;
                    case Driver:
                        eventView.DataContext = new DriverCardViewModel(core as Driver, _coreManager);
                        break;
                    case Environment:
                        eventView.DataContext = new EnvCardViewModel(core as Environment, _coreManager);
                        break;
                    case Executable:
                        eventView.DataContext = new ExecCardViewModel(core as Executable, _coreManager);
                        break;
                    case FileSysIO:
                        eventView.DataContext = new FileIOCardViewModel(core as FileSysIO, _coreManager);
                        break;
                    case PowerShell:
                        eventView.DataContext = new PowerShellCardViewModel(core as PowerShell, _coreManager);
                        break;
                    case ProcessClosure:
                        eventView.DataContext = new ProcClosureCardViewModel(core as ProcessClosure, _coreManager);
                        break;
                    case Registry:
                        eventView.DataContext = new RegCardViewModel(core as Registry, _coreManager);
                        break;
                    case ServiceClosure:
                        eventView.DataContext = new ServiceClosureCardViewModel(core as ServiceClosure, _coreManager);
                        break;
                }
                return eventView;
            }
            else
            {
                throw new ArgumentException($"Invalid type for EventCard IDataTemplate, type provide was: {item.GetType().Name}");
            }
        }

        public bool Match(object? data)
        {
            return data != null && data is EventCore;
        }
    }
}