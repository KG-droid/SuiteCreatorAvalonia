using Log = Logger.Log;

namespace SuiteExecutor
{
    internal partial class Suite
    {
        // Creates (and, if nobody else owns it, immediately acquires) the named mutex for this suite family
        // + action. Returns true when this instance now owns it, i.e. no other instance in the family was
        // running at the moment of the call. Returns false when another live instance already holds it —
        // the mutex handle is still kept in _suiteMutex so the caller can wait on it via WaitForFamilyMutex.
        private bool SetFamilyMutexActive()
        {
            _suiteMutex = new Mutex(true, _familyMutexName, out bool createdNew);
            _ownsMutex = createdNew;
            return createdNew;
        }

        // Blocks until the family mutex (already opened by SetFamilyMutexActive) is released by whichever
        // instance currently owns it, then takes ownership ourselves. Used when an older version of this
        // suite family is actively running: we don't kill it or run alongside it, we wait for it to finish
        // and then proceed so the machine still ends up on this (newer) version.
        private bool WaitForFamilyMutex(TimeSpan timeout)
        {
            if (_suiteMutex == null)
            {
                return false;
            }

            try
            {
                bool acquired = _suiteMutex.WaitOne(timeout);
                _ownsMutex = acquired;
                return acquired;
            }
            catch (AbandonedMutexException)
            {
                // The previous owner's process died (e.g. killed by a shutdown) without releasing it —
                // we still get ownership, and it's safe to proceed since it can no longer be running.
                _ownsMutex = true;
                return true;
            }
        }
    }
}
