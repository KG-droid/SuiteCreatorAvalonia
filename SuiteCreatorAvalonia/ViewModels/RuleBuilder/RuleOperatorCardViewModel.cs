using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;
using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Models.Rules;
namespace SuiteCreatorAvalonia.ViewModels.RuleBuilder
{
    internal partial class RuleOperatorCardViewModel : RuleCardBaseViewModel
    {
        private bool _isTypeChanging = false;

        [ObservableProperty]
        private RuleType _operatorType = RuleType.GroupOpen;

        [ObservableProperty]
        private Grid _operatorContent = new Grid();

        public RuleOperatorCardViewModel()
        {
            SetOperatorContent();
        }

        partial void OnOperatorTypeChanged(RuleType value)
        {
            SetOperatorContent();
        }

        private void SetOperatorContent()
        {
            if (_isTypeChanging) return;
            try
            {
                _isTypeChanging = true;
                OperatorContent.Children.Clear();
                ThemeVariant themeVariant = App.Current?.ActualThemeVariant ?? ThemeVariant.Dark;
                IBrush borderBrush = (Brush)App.Current.FindResource(themeVariant, "SystemControlForegroundBaseHighBrush");
                switch (OperatorType)
                {
                    case RuleType.OR:
                    default:
                        OperatorContent.Children.Add(
                            new TextBlock
                            {
                                Text = "  OR  ",
                                FontSize = 16,
                                FontWeight = FontWeight.Bold,
                                IsHitTestVisible = false,
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            }
                        );
                        break;
                    case RuleType.GroupOpen:
                        OperatorContent.Children.Add(
                            new Border
                            {
                                BorderThickness = new Avalonia.Thickness(2, 2, 2, 0),
                                BorderBrush = borderBrush,
                                IsHitTestVisible = false,
                                Height = 10,
                                Margin = new Avalonia.Thickness(4),
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                            }
                        );
                        break;
                    case RuleType.GroupClose:
                        OperatorContent.Children.Add(
                            new Border
                            {
                                BorderThickness = new Avalonia.Thickness(2, 0, 2, 2),
                                BorderBrush = borderBrush,
                                IsHitTestVisible = false,
                                Height = 10,
                                Margin = new Avalonia.Thickness(4),
                                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                            }
                        );
                        break;
                }
            }
            finally
            {
                _isTypeChanging = false;
            }
        }
    }
}
