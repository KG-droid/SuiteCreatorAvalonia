using CommunityToolkit.Mvvm.ComponentModel;
using Logger;
using SuiteUserPopup.Services;

namespace SuiteUserPopup.ViewModels
{
    public class ViewModelBase : ObservableObject
    {
        protected static Log Logger => AppLogService.Logger;

        protected void LogInfo(string message)
        {
            AppLogService.Info(message, GetType().Name);
        }

        protected void LogWarning(string message)
        {
            AppLogService.Warning(message, GetType().Name);
        }

        protected void LogError(string message)
        {
            AppLogService.Error(message, GetType().Name);
        }
    }
}
