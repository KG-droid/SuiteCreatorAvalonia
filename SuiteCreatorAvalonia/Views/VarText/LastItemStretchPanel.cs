using Avalonia;
using Avalonia.Controls;
using System;

namespace SuiteCreatorAvalonia.Views
{
    public class LastItemStretchPanel : Panel
    {
        protected override Size MeasureOverride(Size availableSize)
        {
            double totalWidth = 0;
            double maxHeight = 0;

            // Measure all children except the last
            for (int i = 0; i < Children.Count; i++)
            {
                var isLast = i == Children.Count - 1;
                var childAvailableWidth = isLast ? Math.Max(0, availableSize.Width - totalWidth) : double.PositiveInfinity;

                var childAvailableSize = new Size(childAvailableWidth, availableSize.Height);
                Children[i].Measure(childAvailableSize);

                if (!isLast)
                    totalWidth += Children[i].DesiredSize.Width;

                maxHeight = Math.Max(maxHeight, Children[i].DesiredSize.Height);
            }

            return new Size(availableSize.Width, maxHeight);
        }

        protected override Size ArrangeOverride(Size finalSize)
        {
            double x = 0;
            double maxHeight = finalSize.Height;

            for (int i = 0; i < Children.Count; i++)
            {
                var child = Children[i];
                bool isLast = i == Children.Count - 1;

                double width = isLast
                    ? Math.Max(0, finalSize.Width - x)
                    : child.DesiredSize.Width;

                child.Arrange(new Rect(x, 0, width, maxHeight));
                x += width;
            }

            return finalSize;
        }
    }
}
