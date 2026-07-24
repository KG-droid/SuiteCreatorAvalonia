using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        private bool SetFamilyMutexActive()
        {
            bool createdNew;
            _suiteMutex = new Mutex(true, _familyMutexName, out createdNew);
            // We only hold the lock when we created the mutex. If it already existed, another family instance
            // owns it and we must not release it later (ReleaseMutex from a non-owner throws).
            _ownsMutex = createdNew;
            if (!createdNew)
            {
                return true;
            }
            return false;
        }
    }
}
