using SuiteCreatorAvalonia.Models.Events;
using SuiteCreatorAvalonia.Models.Package;
using System;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.Services
{
    /// <summary>Lets pages request navigation to a specific Package or Event elsewhere in the suite; wired up by MainViewModel.</summary>
    public class AppNavigationService
    {
        public static AppNavigationService Instance { get; } = new AppNavigationService();

        public Action<PackageBase>? NavigateToPackage { get; set; }
        public Action<EventCore>? NavigateToEvent { get; set; }
        public Action? NavigateToPackagesTab { get; set; }
        public Action? NavigateBack { get; set; }

        /// <summary>Opens the read-only Suite Viewer for a built suite exe; prompts for the file when no path is given.</summary>
        public Func<string?, Task>? ViewBuiltSuite { get; set; }

        /// <summary>Runs the full suite import flow with the given built suite exe pre-selected.</summary>
        public Func<string, Task>? ImportBuiltSuite { get; set; }
    }
}
