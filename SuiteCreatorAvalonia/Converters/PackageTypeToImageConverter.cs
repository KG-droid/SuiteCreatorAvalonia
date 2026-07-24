using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Platform;
using Material.Icons.Avalonia;
using SuiteCreatorAvalonia.Models.Package;
using System;
using System.IO;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace SuiteCreatorAvalonia.Converters
{
    public class PackageTypeToImageConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            if (value is PackageBase)
            {
                Uri? uri = null;
                switch (value)
                {
                    case MSIPkg:
                        uri = new Uri("avares://SuiteCreatorAvalonia/Assets/Images/MSILogo.png");
                        break;
                    case MSIRemoval:
                        uri = new Uri("avares://SuiteCreatorAvalonia/Assets/Images/MSILogo.png");
                        break;
                    case MSIxPkg:
                        uri = new Uri("avares://SuiteCreatorAvalonia/Assets/Images/MSIxLogo.png");
                        break;
                    case MSIxRemoval:
                        uri = new Uri("avares://SuiteCreatorAvalonia/Assets/Images/MSIxLogo.png");
                        break;
                    case OtherBase:
                        uri = new Uri("avares://SuiteCreatorAvalonia/Assets/Images/TerminalLogo.png");
                        break;
                    case OtherRemovalPkg:
                        uri = new Uri("avares://SuiteCreatorAvalonia/Assets/Images/TerminalLogo.png");
                        break;
                }
                using (Stream stream = AssetLoader.Open(uri))
                    if (value.GetType().Name.Contains("Removal", StringComparison.OrdinalIgnoreCase))
                    {
                        return new Grid
                        {
                            Children =
                            {
                                new Avalonia.Controls.Image
                                {
                                    Source = new Bitmap(stream),
                                    Width = 25,
                                    Height = 25,
                                },
                                new MaterialIcon
                                {
                                    Kind = Material.Icons.MaterialIconKind.Close,
                                    Foreground = Avalonia.Media.Brushes.Red,
                                    Width = 25,
                                    Height = 25,
                                    Padding = new Thickness(0),
                                }
                            }
                        };
                    }
                    else
                    {
                        return new Avalonia.Controls.Image
                        {
                            Source = new Bitmap(stream),
                            Width = 25,
                            Height = 25,
                        };
                    }
            }
            return new BindingNotification(new Exception($"Unable to match Type: {value?.GetType().Name}, to a PackageBase"), BindingErrorType.Error);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        {
            return null;
        }
    }
}