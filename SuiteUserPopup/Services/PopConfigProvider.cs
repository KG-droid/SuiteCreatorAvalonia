using System;
using SuiteUserPopup.Models.Config;

namespace SuiteUserPopup.Services;

public static class PopConfigProvider
{
    public static PopConfig? Current { get; private set; }

    public static event EventHandler? Changed;

    public static void Set(PopConfig? config)
    {
        Current = config;
        Changed?.Invoke(null, EventArgs.Empty);
    }
}
