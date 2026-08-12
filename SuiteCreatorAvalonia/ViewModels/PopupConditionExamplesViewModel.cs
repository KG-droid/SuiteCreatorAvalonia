using Avalonia.Controls;
using Avalonia.Input.Platform;
using AvaloniaEdit.Document;
using CommunityToolkit.Mvvm.Input;
using SuiteCreatorAvalonia.Services;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    // Read-only reference dialog explaining how the admin's Global Popup Condition (Settings page)
    // and a suite's own popup Condition script (Popups page) combine - both must return $True for
    // the popup to show, so packagers need to see worked examples of each on its own and together.
    internal partial class PopupConditionExamplesViewModel : ViewModelBase
    {
        public TextDocument GlobalOnly_Global { get; } = new(
            "# Never show popups on kiosk/robot devices, regardless of any suite condition\n" +
            "if ($env:COMPUTERNAME -like \"ROBOT*\") {\n" +
            "    Write-Output $False\n" +
            "} else {\n" +
            "    Write-Output $True\n" +
            "}");

        public TextDocument GlobalOnly_Popup { get; } = new("(no condition script set on the popup page)");

        public TextDocument PopupOnly_Global { get; } = new("(no global condition set in Settings)");

        public TextDocument PopupOnly_Popup { get; } = new(
            "# Only show the upgrade popup while a VPN connection is active\n" +
            "$vpn = Get-NetAdapter | Where-Object {\n" +
            "    $_.InterfaceDescription -like \"*VPN*\" -and $_.Status -eq \"Up\"\n" +
            "}\n" +
            "if ($vpn) {\n" +
            "    Write-Output $True\n" +
            "} else {\n" +
            "    Write-Output $False\n" +
            "}");

        public TextDocument Both_Global { get; } = new(
            "# Company-wide rule: never show popups on kiosk/robot devices\n" +
            "if ($env:COMPUTERNAME -like \"ROBOT*\") {\n" +
            "    Write-Output $False\n" +
            "} else {\n" +
            "    Write-Output $True\n" +
            "}");

        public TextDocument Both_Popup { get; } = new(
            "# This suite's own rule: only upgrade while a VPN connection is active\n" +
            "$vpn = Get-NetAdapter | Where-Object {\n" +
            "    $_.InterfaceDescription -like \"*VPN*\" -and $_.Status -eq \"Up\"\n" +
            "}\n" +
            "if ($vpn) {\n" +
            "    Write-Output $True\n" +
            "} else {\n" +
            "    Write-Output $False\n" +
            "}");

        [RelayCommand]
        private async Task CopyScript(string? script)
        {
            if (string.IsNullOrEmpty(script)) return;

            TopLevel? topLevel = DialogManager.GetTopLevelForContext(this);
            if (topLevel?.Clipboard != null)
            {
                await topLevel.Clipboard.SetTextAsync(script);
            }
        }

        [RelayCommand]
        private void Close()
        {
            this.CloseDialog();
        }
    }
}
