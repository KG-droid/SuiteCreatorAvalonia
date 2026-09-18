using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Services;
using SuiteOperations;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    /// <summary>
    /// Read-only page that shows what a built suite exe contains (packages, events, rules, popup and build
    /// settings) by reading just its SuiteConfig.scfg - nothing is extracted and the open project is untouched.
    /// Offers the full import if the user then decides they want to edit the suite.
    /// </summary>
    public partial class SuiteConfigViewerViewModel : ViewModelBase
    {
        [ObservableProperty]
        private string? _suiteExePath;

        [ObservableProperty]
        private string? _suiteFileName;

        [ObservableProperty]
        private string? _projectName;

        [ObservableProperty]
        private string? _buildSummary;

        [ObservableProperty]
        private string? _countsSummary;

        [ObservableProperty]
        private string? _loadError;

        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(ImportSuiteCommand))]
        private bool _isLoaded;

        [ObservableProperty]
        private bool _isLoading;

        public ObservableCollection<SuiteConfigSection> Sections { get; } = new ObservableCollection<SuiteConfigSection>();

        public bool HasLoadError => !string.IsNullOrWhiteSpace(LoadError);

        public bool HasBuildSummary => !string.IsNullOrWhiteSpace(BuildSummary);

        // Parameterless constructor for design-time support and the DI view factory
        public SuiteConfigViewerViewModel()
        {
        }

        partial void OnLoadErrorChanged(string? value) => OnPropertyChanged(nameof(HasLoadError));

        partial void OnBuildSummaryChanged(string? value) => OnPropertyChanged(nameof(HasBuildSummary));

        /// <summary>Reads the config out of the given built suite exe and populates the page. Errors are shown inline.</summary>
        public async Task LoadSuiteAsync(string suiteExePath)
        {
            IsLoading = true;
            IsLoaded = false;
            LoadError = null;
            Sections.Clear();
            SuiteExePath = suiteExePath;
            SuiteFileName = Path.GetFileName(suiteExePath);
            ProjectName = null;
            BuildSummary = null;
            CountsSummary = null;

            try
            {
                AppLog.Info($"Reading suite config for viewing from: {suiteExePath}", "SuiteViewer");
                SuiteExecConfig config = await SuiteImport.ReadSuiteConfigAsync(suiteExePath);
                SuiteConfigSummary summary = SuiteConfigSummariser.Summarise(config);

                ProjectName = string.IsNullOrWhiteSpace(summary.ProjectName)
                    ? Path.GetFileNameWithoutExtension(suiteExePath)
                    : summary.ProjectName;
                BuildSummary = summary.BuildSummary;
                CountsSummary = $"{Plural(summary.PackageCount, "package", "packages")}, " +
                                $"{Plural(summary.EventCount, "event", "events")}, " +
                                $"{Plural(summary.RuleSetCount, "rule set", "rule sets")}";
                foreach (SuiteConfigSection section in summary.Sections)
                {
                    Sections.Add(section);
                }
                IsLoaded = true;
            }
            catch (Exception ex)
            {
                AppLog.Error($"Failed to read suite config for viewing from: {suiteExePath}", ex, "SuiteViewer");
                LoadError = ex.Message;
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        private async Task BrowseSuite()
        {
            Func<string?, Task>? viewBuiltSuite = AppNavigationService.Instance.ViewBuiltSuite;
            if (viewBuiltSuite != null)
                await viewBuiltSuite(null);
        }

        [RelayCommand(CanExecute = nameof(IsLoaded))]
        private async Task ImportSuite()
        {
            if (!IsLoaded || string.IsNullOrWhiteSpace(SuiteExePath)) return;
            Func<string, Task>? importBuiltSuite = AppNavigationService.Instance.ImportBuiltSuite;
            if (importBuiltSuite != null)
                await importBuiltSuite(SuiteExePath);
        }

        [RelayCommand]
        private void Close()
        {
            Action? navigateBack = AppNavigationService.Instance.NavigateBack;
            if (navigateBack != null)
                navigateBack();
            else
                AppNavigationService.Instance.NavigateToPackagesTab?.Invoke();
        }

        private static string Plural(int count, string singular, string plural) => $"{count} {(count == 1 ? singular : plural)}";
    }
}
