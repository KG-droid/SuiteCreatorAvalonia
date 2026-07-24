using Avalonia.Controls;

namespace SuiteCreatorAvalonia.Views
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();
            Services.HelpService.Instance.Overlay = HelpOverlay;
        }
    }
}