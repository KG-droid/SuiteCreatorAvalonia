using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Models.Package;
using System;

namespace SuiteCreatorAvalonia.Services
{
    /// <summary>Lets pages request navigation to a specific Package or Event elsewhere in the suite; wired up by MainViewModel.</summary>
    public class AppNavigationService
    {
        public static AppNavigationService Instance { get; } = new AppNavigationService();

        public Action<PackageBase>? NavigateToPackage { get; set; }
        public Action<EventCore>? NavigateToEvent { get; set; }
    }
}
