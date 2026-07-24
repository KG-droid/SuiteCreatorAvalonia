using Avalonia.Media;
using Material.Icons;

namespace SuiteCreatorAvalonia.Models.Common
{
    public enum PopupAction
    {
        Continue,
        Skip,
    }

    public enum MimeType
    {
        Bmp,
        Gif
    }

    public class Popup
    {
        public bool ShowProgress { get; set; }
        public bool ShowPopupWarning { get; set; }
        public bool LinkToProcClosures { get; set; }
        public bool HasPSCondition { get; set; }
        public string? PSCondition { get; set; }
        public bool HasGlobalPSCondition { get; set; }
        public string? GlobalPSCondition { get; set; }
        public bool HasInstallTxt { get; set; }
        public string? InstallTxt { get; set; }
        public bool HasUninstallTxt { get; set; }
        public string? UninstallTxt { get; set; }
        public int Timer { get; set; }
        public PopupAction? TimerExpireAction { get; set; }
        public int? DelayDays { get; set; }
        public PopupAction? LoggedOffAction { get; set; }
        public PopupAction? LockedAction { get; set; }
        public PopupAction? ESPAction { get; set; }
        public MaterialIconKind DeployIconKind { get; set; }
        public MaterialIconKind RemoveIconKind { get; set; }
        public string? InstAction { get; set; }
        public string? UninstAction { get; set; }
        public string? SuiteLogoBase64 { get; set; }
        public MimeType? CompanyLogoType { get; set; }
        public Color? BackgroundColor { get; set; }

        public Popup Clone()
        {
            return new Popup
            {
                ShowProgress = ShowProgress,
                ShowPopupWarning = ShowPopupWarning,
                LinkToProcClosures = LinkToProcClosures,
                HasPSCondition = HasPSCondition,
                PSCondition = PSCondition,
                HasGlobalPSCondition = HasGlobalPSCondition,
                GlobalPSCondition = GlobalPSCondition,
                HasInstallTxt = HasInstallTxt,
                InstallTxt = InstallTxt,
                HasUninstallTxt = HasUninstallTxt,
                UninstallTxt = UninstallTxt,
                Timer = Timer,
                TimerExpireAction = TimerExpireAction,
                DelayDays = DelayDays,
                LoggedOffAction = LoggedOffAction,
                LockedAction = LockedAction,
                ESPAction = ESPAction,
                DeployIconKind = DeployIconKind,
                RemoveIconKind = RemoveIconKind,
                InstAction = InstAction,
                UninstAction = UninstAction,
                CompanyLogoType = CompanyLogoType,
                BackgroundColor = BackgroundColor,
                SuiteLogoBase64 = SuiteLogoBase64
            };
        }

        public void UpdateFrom(Popup other)
        {
            if (other == null) return;

            ShowProgress = other.ShowProgress;
            ShowPopupWarning = other.ShowPopupWarning;
            LinkToProcClosures = other.LinkToProcClosures;
            HasPSCondition = other.HasPSCondition;
            PSCondition = other.PSCondition;
            HasGlobalPSCondition = other.HasGlobalPSCondition;
            GlobalPSCondition = other.GlobalPSCondition;
            HasInstallTxt = other.HasInstallTxt;
            InstallTxt = other.InstallTxt;
            HasUninstallTxt = other.HasUninstallTxt;
            UninstallTxt = other.UninstallTxt;
            Timer = other.Timer;
            TimerExpireAction = other.TimerExpireAction;
            DelayDays = other.DelayDays;
            LoggedOffAction = other.LoggedOffAction;
            LockedAction = other.LockedAction;
            ESPAction = other.ESPAction;
            DeployIconKind = other.DeployIconKind;
            RemoveIconKind = other.RemoveIconKind;
            InstAction = other.InstAction;
            UninstAction = other.UninstAction;
            CompanyLogoType = other.CompanyLogoType;
            BackgroundColor = other.BackgroundColor;
            SuiteLogoBase64 = other.SuiteLogoBase64;
        }
    }
}
