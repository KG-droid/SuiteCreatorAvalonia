using SuiteCreatorAvalonia.Enums;

namespace SuiteCreatorAvalonia.Models.Common
{
    public class Build
    {
        public string? Manufacturer { get; set; }
        public string? Name { get; set; }
        public Version? SuiteVersion { get; set; }
        public int Revision { get; set; }
        public Guid UpgradeCode { get; set; }
        public bool FailSafe { get; set; }
        public bool RefreshEnv { get; set; }
        public bool KeepCache { get; set; }
        public bool FailureRetry { get; set; }
        public bool RestartOnFailure { get; set; }
        public bool ReverseUninstall { get; set; }
        public string? LogDir { get; set; }

        public Architecture Architecture = Architecture.x64;
        public string? Detection { get; set; }

        public Build Clone()
        {
            return new Build
            {
                Manufacturer = Manufacturer,
                Name = Name,
                SuiteVersion = SuiteVersion,
                Revision = Revision,
                UpgradeCode = UpgradeCode,
                FailSafe = FailSafe,
                RefreshEnv = RefreshEnv,
                KeepCache = KeepCache,
                FailureRetry = FailureRetry,
                RestartOnFailure = RestartOnFailure,
                ReverseUninstall = ReverseUninstall,
                Architecture = Architecture,
                Detection = Detection,
                LogDir = LogDir
            };
        }

        public void UpdateFrom(Build updated)
        {
            if (updated == null) throw new ArgumentNullException(nameof(updated));
            Manufacturer = updated.Manufacturer;
            Name = updated.Name;
            SuiteVersion = updated.SuiteVersion;
            Revision = updated.Revision;
            UpgradeCode = updated.UpgradeCode;
            FailSafe = updated.FailSafe;
            RefreshEnv = updated.RefreshEnv;
            KeepCache = updated.KeepCache;
            FailureRetry = updated.FailureRetry;
            RestartOnFailure = updated.RestartOnFailure;
            ReverseUninstall = updated.ReverseUninstall;
            Architecture = updated.Architecture;
            Detection = updated.Detection;
            LogDir = updated.LogDir;
        }
    }
}
