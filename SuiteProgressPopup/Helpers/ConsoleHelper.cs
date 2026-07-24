using System;
using System.Runtime.InteropServices;

public static class ConsoleHelper
{
    [DllImport("kernel32.dll")]
    public static extern bool AttachConsole(int dwProcessId);

    public const int ATTACH_PARENT_PROCESS = -1;

    public static void AttachToParentConsole()
    {
        AttachConsole(ATTACH_PARENT_PROCESS);
    }
}
