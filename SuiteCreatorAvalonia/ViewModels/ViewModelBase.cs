using Avalonia.Controls;
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using SuiteCreatorAvalonia.Services;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.ViewModels
{
    public class ViewModelBase : ObservableValidator
    {
        public async Task ShowErrorPop(string errorTitle, string errorBody)
        {
            StackPanel diagHostStack = new StackPanel()
            {
                Margin = new Avalonia.Thickness(16),
            };

            diagHostStack.Children.Add(new TextBlock()
            {
                Text = errorTitle,
                FontWeight = FontWeight.ExtraBold,
                Margin = new Avalonia.Thickness(0, 0, 0, 8),
            });

            ScrollViewer scrollViewer = new ScrollViewer()
            {
                VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                Content = new TextBlock()
                {
                    Text = errorBody,
                    TextWrapping = TextWrapping.Wrap,
                },
            };
            diagHostStack.Children.Add(scrollViewer);

            Button okButton = new Button()
            {
                Content = "  OK  ",
                HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Right,
                HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                Width = 80,
                Margin = new Avalonia.Thickness(0, 10, 0, 0),
            };
            okButton.Click += (s, e) =>
            {
                this.CloseDialog("MainDialogHost");
            };
            diagHostStack.Children.Add(okButton);

            // Constrain to 50% of the window once attached to the visual tree
            diagHostStack.AttachedToVisualTree += (s, e) =>
            {
                TopLevel? topLevel = TopLevel.GetTopLevel(diagHostStack);
                if (topLevel != null)
                {
                    double maxW = topLevel.Width * 0.5;
                    double maxH = topLevel.Height * 0.5;
                    diagHostStack.MaxWidth = double.IsNaN(maxW) || maxW <= 0 ? 400 : maxW;
                    diagHostStack.MaxHeight = double.IsNaN(maxH) || maxH <= 0 ? 300 : maxH;
                    scrollViewer.MaxHeight = diagHostStack.MaxHeight - 80; // leave room for title + button
                }
            };

            await this.ShowDialogAsync(diagHostStack);
        }
    }
}
