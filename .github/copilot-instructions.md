# Copilot Instructions

## Project Guidelines
- The user prefers explicit type declarations (e.g., DataTransfer dataTransfer) over the use of 'var', and prefers short type names, with a using statement at the top (e.g., SHA256, using System.Security.Cryptography;) instead of fully-qualified names like System.Security.Cryptography.SHA256; when writing code.
- Keep debug-only logic enclosed in `#if DEBUG`/`#endif` so no debug code is included in release/live builds.