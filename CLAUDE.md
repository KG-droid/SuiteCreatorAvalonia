# Project instructions for Claude

## Git workflow

- Before committing any change, always ask the user whether to create a new branch or commit to an existing one (and which). Do not assume the branch, even if the session or task names a default.

## Built suite config

- The creator must never store the admin's (build machine's) paths in the built suite config. Files are bundled into the suite's zip, and `SuiteExecutor/Suite.PathResolution.cs` resolves each event's path from that bundled folder at run time. In `SuiteBuilder`, leave path fields such as `FontPath`, `InfPath`, `FilePath`, `RegFilePath`, `ExtPath` and `ScriptPath` unset (or relative), and derive anything needed later (e.g. `FontFileName`, `InfName`) from the real path before dropping it.
