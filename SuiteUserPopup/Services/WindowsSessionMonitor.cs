using Microsoft.Win32;
using System;

namespace SuiteUserPopup.Services
{
    internal sealed class WindowsSessionMonitor : IDisposable
    {
        public event EventHandler<bool>? SessionLockedChanged;

        public bool IsSessionLocked { get; private set; }

        public WindowsSessionMonitor()
        {
            IsSessionLocked = false;
            SystemEvents.SessionSwitch += OnSessionSwitch;
        }

        private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
        {
            switch (e.Reason)
            {
                case SessionSwitchReason.SessionLock:
                    SetLocked(true);
                    break;
                case SessionSwitchReason.SessionUnlock:
                    SetLocked(false);
                    break;
            }
        }

        private void SetLocked(bool locked)
        {
            if (IsSessionLocked == locked)
                return;

            IsSessionLocked = locked;
            SessionLockedChanged?.Invoke(this, locked);
        }

        public void Dispose()
        {
            SystemEvents.SessionSwitch -= OnSessionSwitch;
        }
    }
}
