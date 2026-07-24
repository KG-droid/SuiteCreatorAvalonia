using Logger;
using SuiteCreatorAvalonia.Models.Package;
using SuiteTools;
using Windows.Management.Deployment;

namespace SuiteOperations.Package
{
    public class MSIxRemovalExec : MSIxRemoval
    {
        private Log _log;

        public MSIxRemovalExec(Log log)
        {
            _log = log;
        }

        public MSIxRemovalExec() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        public void Execute()
        {
            Validate();
            _log.WriteLog($"Running MSIx package removal for: {PFN}");
            MSIxTools.UninstallMSIxPackageAsync(PFN, IsSpecificVersionRemoval).GetAwaiter().GetResult();
            _log.WriteLog($"Removal complete");
        }

        public new void Validate()
        {
            if (string.IsNullOrEmpty(PFN))
            {
                throw new ArgumentNullException("Neither Package Family Name or Package Full Name was provided", nameof(PFN));
            }
        }

        public PackageExecDetectionResult IsRemovalRequired()
        {
            if (string.IsNullOrEmpty(PFN))
                throw new Exception("Neither Package Family Name or Package Full Name was provided");
            PackageExecDetectionResult result = new();
            result.Summary = $"Checking if MSIx package with PFN: {PFN} is installed for any user.";
            result.Result = MSIxTools.IsMSIReadyForAnyUser(PFN);
            return result;
        }
    }
}
