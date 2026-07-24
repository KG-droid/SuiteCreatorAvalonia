using Avalonia.Controls;
using SuiteCreatorAvalonia.Enums;
using System;
using System.Linq;

namespace SuiteCreatorAvalonia.Views;

public partial class ServClosuresView : UserControl
{
    public ServClosuresView()
    {
        InitializeComponent();
        Resources["ClosureTypes"] = Enum.GetValues(typeof(ClosureType)).Cast<ClosureType>();
    }
}