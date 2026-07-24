using Logger;
using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Events;
using SuiteTools;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.ServiceProcess;
using System.Threading;

namespace SuiteOperations.Events
{
    public partial class ServiceClosureExecEvent : ServiceClosure
    {
        private const int ServiceOperationMaxRetries = 3;
        private const int ServiceOperationRetryDelayMs = 5000;
        private static readonly TimeSpan ServiceOperationTimeout = TimeSpan.FromSeconds(30);
        private Log _log;
        private string? _suiteId;

        public ServiceClosureExecEvent(Log log)
        {
            _log = log;
        }

        public ServiceClosureExecEvent() { }

        public void SetLog(Log log)
        {
            _log = log;
        }

        public void SetSuiteId(string suiteId)
        {
            _suiteId = suiteId;
        }

        public void ExecuteEvent()
        {
            List<string> serviceNames = GetNames().ToList();
            if (serviceNames.Count == 0)
            {
                _log.WriteLog("Service name is empty.", "ServiceClosureExecEvent", Log.Severity.Error);
                throw new InvalidOperationException("Service name is empty.");
            }

            foreach (string serviceName in serviceNames)
            {
                ExecuteForService(serviceName);
            }
        }

        private void ExecuteForService(string serviceName)
        {
            try
            {
                // Failsafe setup MUST succeed before actually stopping/disabling the service — if it can't be
                // established, the service could be left permanently down if the suite is killed before it's
                // restored. Captured once, outside the retry loop below, so a retry after a partial failure
                // doesn't re-capture an already-changed state as if it were the original.
                if (Type == ClosureType.Stop || Type == ClosureType.Disable)
                {
                    RegisterFailsafe(serviceName);
                }

                for (int attempt = 1; attempt <= ServiceOperationMaxRetries; attempt++)
                {
                    try
                    {
                        using (ServiceController sc = new ServiceController(serviceName))
                        {
                            sc.Refresh();

                            switch (Type)
                            {
                                case ClosureType.Stop:
                                    if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                                    {
                                        _log.WriteLog($"Stopping service: {serviceName}");
                                        sc.Stop();
                                        sc.WaitForStatus(ServiceControllerStatus.Stopped, ServiceOperationTimeout);
                                    }
                                    else
                                    {
                                        _log.WriteLog($"Service {serviceName} is already stopped.");
                                    }
                                    break;
                                case ClosureType.Disable:
                                    _log.WriteLog($"Disabling service: {serviceName}");
                                    SetServiceStartMode(serviceName, ServiceStartMode.Disabled);
                                    if (sc.Status != ServiceControllerStatus.Stopped && sc.Status != ServiceControllerStatus.StopPending)
                                    {
                                        sc.Stop();
                                        sc.WaitForStatus(ServiceControllerStatus.Stopped, ServiceOperationTimeout);
                                    }
                                    break;
                                case ClosureType.EnableAndStart:
                                    _log.WriteLog($"Enabling and starting service: {serviceName}");
                                    SetServiceStartMode(serviceName, ServiceStartMode.Automatic);
                                    if (sc.Status != ServiceControllerStatus.Running && sc.Status != ServiceControllerStatus.StartPending)
                                    {
                                        sc.Start();
                                        sc.WaitForStatus(ServiceControllerStatus.Running, ServiceOperationTimeout);
                                    }
                                    // This service no longer needs failsafe protection now that it's been
                                    // explicitly restored — remove it before a later, unrelated crash could
                                    // cause the failsafe to revert this deliberate restore back to the state
                                    // captured before the original Stop/Disable.
                                    ClearFailsafe(serviceName);
                                    break;
                                default:
                                    _log.WriteLog($"Unknown service closure type: {Type}");
                                    throw new InvalidOperationException($"Unknown service closure type: {Type}");
                            }
                        }

                        return;
                    }
                    catch (Exception ex) when (attempt < ServiceOperationMaxRetries && IsRetryableServiceException(ex))
                    {
                        _log.WriteLog($"Service operation for '{serviceName}' failed on attempt {attempt}. Retrying in {ServiceOperationRetryDelayMs / 1000} seconds. Error: {ex.Message}", "ServiceClosureExecEvent", Log.Severity.Warning);
                        Thread.Sleep(ServiceOperationRetryDelayMs);
                    }
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Exception in ExecuteEvent for service: {serviceName}, {ex.Message}", "ServiceClosureExecEvent", Log.Severity.Error);
                throw;
            }
        }

        private void RegisterFailsafe(string serviceName)
        {
            bool wasRunning;
            using (ServiceController sc = new ServiceController(serviceName))
            {
                sc.Refresh();
                wasRunning = sc.Status == ServiceControllerStatus.Running || sc.Status == ServiceControllerStatus.StartPending;
            }
            uint priorStartType = ClosureFailsafe.GetServiceStartType(serviceName);

            string taskName = ClosureFailsafe.SharedFailsafeTaskName();
            string blockListPath = ClosureFailsafe.SharedBlockListPath(taskName, _suiteId);

            ClosureFailsafe.EnsureFailsafeTaskAndWatcher(_log, taskName, blockListPath);
            ClosureFailsafe.AddServiceEntry(blockListPath, serviceName, priorStartType, wasRunning);
            _log.WriteLog($"Registered failsafe restore for service '{serviceName}' (prior start type: {(NativeMethods.ServiceStartType)priorStartType}, was running: {wasRunning})", "ServiceClosureExecEvent");
        }

        // Removes this service's pending failsafe entry without touching the service itself. Called both when
        // EnableAndStart explicitly restores a service, and unconditionally at the end of every suite run (see
        // Suite.cs) so a suite that finishes on its own — with or without ever running EnableAndStart — doesn't
        // leave a background ONSTART task watching a service indefinitely (that would wrongly undo a
        // deliberately permanent Stop/Disable on some unrelated future reboot).
        public void ClearFailsafe(string serviceName)
        {
            string taskName = ClosureFailsafe.SharedFailsafeTaskName();
            string blockListPath = ClosureFailsafe.SharedBlockListPath(taskName, _suiteId);

            ClosureFailsafe.RemoveServiceEntry(blockListPath, serviceName);

            if (ClosureFailsafe.ListEmpty(blockListPath))
            {
                ClosureFailsafe.DeleteFailsafeTask(taskName, blockListPath);
                _log.WriteLog($"Cleaned up shared failsafe task: {taskName}", "ServiceClosureExecEvent");
            }
        }

        // Called unconditionally at the end of every suite run (success or failure) for every configured
        // Stop/Disable event — see remarks on ClearFailsafe(string).
        public void ClearFailsafe()
        {
            if (Type != ClosureType.Stop && Type != ClosureType.Disable)
                return;

            foreach (string serviceName in GetNames())
            {
                try
                {
                    ClearFailsafe(serviceName);
                }
                catch (Exception ex)
                {
                    _log.WriteLog($"Failed to clear failsafe for service '{serviceName}': {ex.Message}", "ServiceClosureExecEvent", Log.Severity.Warning);
                }
            }
        }

        private static bool IsRetryableServiceException(Exception ex)
        {
            if (ex is System.TimeoutException || ex is System.ServiceProcess.TimeoutException)
            {
                return true;
            }

            if (ex is InvalidOperationException && ex.InnerException is Win32Exception)
            {
                return true;
            }

            return false;
        }

        private void SetServiceStartMode(string serviceName, ServiceStartMode mode)
        {
            nint scm = nint.Zero;
            nint svc = nint.Zero;
            try
            {
                scm = NativeMethods.OpenSCManager(null, null, NativeMethods.SC_MANAGER_ALL_ACCESS);
                if (scm == nint.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    _log.WriteLog($"OpenSCManager failed: {err}", "ServiceClosureExecEvent", Log.Severity.Error);
                    throw new InvalidOperationException($"OpenSCManager failed: {err}");
                }
                svc = NativeMethods.OpenService(scm, serviceName, NativeMethods.SERVICE_CHANGE_CONFIG | NativeMethods.SERVICE_QUERY_CONFIG);
                if (svc == nint.Zero)
                {
                    int err = Marshal.GetLastWin32Error();
                    _log.WriteLog($"OpenService failed for '{serviceName}': {err}", "ServiceClosureExecEvent", Log.Severity.Error);
                    throw new InvalidOperationException($"OpenService failed for '{serviceName}': {err}");
                }
                NativeMethods.ServiceStartType startType = mode == ServiceStartMode.Disabled ? NativeMethods.ServiceStartType.Disabled : NativeMethods.ServiceStartType.Automatic;
                bool result = NativeMethods.ChangeServiceConfig(
                    svc,
                    NativeMethods.SERVICE_NO_CHANGE,
                    (uint)startType,
                    NativeMethods.SERVICE_NO_CHANGE,
                    null,
                    null,
                    nint.Zero,
                    null,
                    null,
                    null,
                    null);
                if (!result)
                {
                    int err = Marshal.GetLastWin32Error();
                    _log.WriteLog($"ChangeServiceConfig failed for '{serviceName}': {err}", "ServiceClosureExecEvent", Log.Severity.Error);
                    throw new InvalidOperationException($"ChangeServiceConfig failed for '{serviceName}': {err}");
                }
                else
                {
                    _log.WriteLog($"Set service '{serviceName}' start mode to {mode}.", "ServiceClosureExecEvent");
                }
            }
            catch (Exception ex)
            {
                _log.WriteLog($"Failed to set start mode for service '{serviceName}': {ex.Message}", "ServiceClosureExecEvent", Log.Severity.Error);
                throw;
            }
            finally
            {
                if (svc != nint.Zero) NativeMethods.CloseServiceHandle(svc);
                if (scm != nint.Zero) NativeMethods.CloseServiceHandle(scm);
            }
        }
    }
}
