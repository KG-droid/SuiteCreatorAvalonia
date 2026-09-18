using Avalonia.Controls;
using Avalonia.Interactivity;
using AvaloniaEdit.Document;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.ViewModels;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.Views
{
    public partial class SuiteConfigViewerView : UserControl
    {
        public SuiteConfigViewerView()
        {
            InitializeComponent();
        }

        private void ViewScript_Click(object? sender, RoutedEventArgs e)
        {
            if (sender is Button { DataContext: SuiteConfigItem item } && item.HasScriptContent)
            {
                _ = OpenScriptViewerAsync(item);
            }
        }

        // Opens the existing PowerShell editor window in read-only mode, populated with just the script
        // text already held in memory from the loaded suite config - no further read of the suite exe.
        private async Task OpenScriptViewerAsync(SuiteConfigItem item)
        {
            Window? parent = TopLevel.GetTopLevel(this) as Window;
            if (parent == null) return;

            PowerShellWindowViewModel vm = new PowerShellWindowViewModel
            {
                IsReadOnly = true,
                ScriptName = item.Title,
                ScriptArgs = item.ScriptArgs,
                Context = item.ScriptContext,
                ScriptDoc = new TextDocument(item.ScriptContent ?? string.Empty),
            };

            Window psWindow = new PowerShellWindow
            {
                Height = parent.Height * 0.8,
                Width = parent.Width * 0.8,
                DataContext = vm,
            };
            await psWindow.ShowDialog(parent);
        }
    }
}
