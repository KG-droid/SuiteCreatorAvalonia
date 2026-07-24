using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Services;
using System.Diagnostics;
using System.Reflection;

namespace SuiteCreatorAvalonia.ViewModels
{
    public partial class AboutViewModel : ViewModelBase
    {
        public string AppTitle => "Suite Creator";

        public string VersionText
        {
            get
            {
                System.Version? version = Assembly.GetEntryAssembly()?.GetName().Version;
                return version == null ? "Version unknown" : $"Version {version.ToString(3)}";
            }
        }

        public string Author => "Created by KG-droid";

        public string Description =>
            "Suite Creator lets admins build a single deployable suite that installs multiple applications, " +
            "along with registry keys, files, certificates, shortcuts, environment variables, PowerShell scripts and more. " +
            "The built suite is a single executable with its own detection rule, ready for deployment tools such as Intune or SCCM.";

        public string RepoUrl => "https://github.com/KG-droid/SuiteCreatorAvalonia";

        public string LicenseText => "Free and open source";

        [RelayCommand]
        public void OpenRepo()
        {
            Process.Start(new ProcessStartInfo(RepoUrl) { UseShellExecute = true });
        }

        [RelayCommand]
        public void Close()
        {
            this.CloseDialog();
        }
    }
}
