using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogHostAvalonia;
using System.Collections.ObjectModel;

namespace SuiteCreatorAvalonia.ViewModels
{
    public partial class DialogHostViewModel : ViewModelBase
    {
        [ObservableProperty]
        public string _title = "Popup";

        [ObservableProperty]
        public string _msg = "Your message shown here";

        [ObservableProperty]
        public MsgTypes _msgType;

        [ObservableProperty]
        public PopupTypes _popType;

        [ObservableProperty]
        public string? _affirmativeText;

        [ObservableProperty]
        public string? _negativeText;

        [ObservableProperty]
        public string? _questionResponse;
        public ObservableCollection<Button> popButtons { get; set; } = new ObservableCollection<Button>();

        // Parameterless constructor for design-time support
        public DialogHostViewModel() : this("Example Title", "Example Message", MsgTypes.Info, PopupTypes.Message)
        {
        }
        public DialogHostViewModel(
            string title, 
            string msg, 
            MsgTypes msgType, 
            PopupTypes popupType = PopupTypes.Message, 
            string? affirmativeText = null, 
            string? negativeText = null, 
            bool showCancel = true, 
            string? affirmativeBackground = null, 
            string? negativeBackground = null)
        {
            Title = title;
            Msg = msg;
            MsgType = msgType;
            PopType = popupType;
            AffirmativeText = affirmativeText;
            NegativeText = negativeText;
            Thickness padding = new Avalonia.Thickness(15, 5, 15, 5);
            Thickness margin = new Avalonia.Thickness(1, 0, 1, 0);
            switch (popupType)
            {
                case PopupTypes.Message:
                    Button button = new Button();
                    button.Padding = padding;
                    button.Margin = margin;
                    button.Content = "   Ok   ";
                    button.Bind(
                        Button.CommandProperty,
                        new Binding { Path = "CloseDialogCommand", RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(DialogHost) } }
                    );
                    popButtons.Add(button);
                    break;
                case PopupTypes.Question:
                    Button afButton = new Button();
                    afButton.Padding = padding;
                    afButton.Content = AffirmativeText;
                    afButton.Margin = margin;
                    afButton.FontWeight = FontWeight.Bold;
                    afButton.Bind(
                        Button.BackgroundProperty,
                        new Binding { Path = "SystemControlBackgroundListMediumBrush", Source = Application.Current.FindResource("SystemControlBackgroundListMediumBrush") }
                    );
                    afButton.Bind(
                        Button.CommandProperty,
                        new Binding { Path = "CloseDialogCommand", RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(DialogHost) } }
                    );
                    afButton.Bind(
                        Button.BackgroundProperty,
                        new Binding { Path = "AccentColor", Source = Application.Current.Styles[0] }
                    );
                    if (!string.IsNullOrWhiteSpace(affirmativeBackground))
                    {
                        try
                        {
                            afButton.Background = SolidColorBrush.Parse(affirmativeBackground);
                        }
                        catch { }
                    }
                    afButton.CommandParameter = AffirmativeText;
                    popButtons.Add(afButton);

                    Button negButton = new Button();
                    negButton.Padding = padding;
                    negButton.Content = new TextBlock { Text = negativeText };
                    negButton.Margin = margin;
                    negButton.Bind(
                        Button.CommandProperty,
                        new Binding { Path = "CloseDialogCommand", RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(DialogHost) } }
                    );
                    // If a specific negativeBackground hex was provided, use it. Otherwise bind to a lighter
                    // variant of the app AccentColor so the safer option is highlighted.
                    if (!string.IsNullOrWhiteSpace(negativeBackground))
                    {
                        try
                        {
                            negButton.Background = SolidColorBrush.Parse(negativeBackground);
                        }
                        catch { }
                    }
                    else
                    {
                        negButton.Bind(
                            Button.BackgroundProperty,
                            new Binding { Path = "AccentColor", Source = Application.Current.Styles[0], Converter = new LightenBrushConverter() }
                        );
                    }
                    negButton.CommandParameter = NegativeText;
                    // Make the negative (safer) option stand out and be the default action
                    negButton.IsDefault = true;
                    negButton.FontWeight = FontWeight.Bold;
                    negButton.Foreground = Brushes.White;
                    popButtons.Add(negButton);

                    if (showCancel)
                    {
                        Button cancelButton = new Button();
                        cancelButton.Padding = padding;
                        cancelButton.Margin = margin;
                        cancelButton.Content = "Cancel";
                        cancelButton.Bind(
                            Button.CommandProperty,
                            new Binding { Path = "CloseDialogCommand", RelativeSource = new RelativeSource(RelativeSourceMode.FindAncestor) { AncestorType = typeof(DialogHost) } }
                        );
                        cancelButton.Bind(
                            Button.BackgroundProperty,
                            new Binding { Path = "AccentColor", Source = Application.Current.Styles[0] }
                        );
                        cancelButton.CommandParameter = "Cancel";
                        popButtons.Add(cancelButton);
                    }
                    break;
            }
        }

    // Converter to produce a lighter variant of a provided brush/color.
    internal class LightenBrushConverter : IValueConverter
    {
        // factor can be provided via parameter as double (0-1) to control how much lighter; default 0.25
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            double factor = 0.25;
            if (parameter is string pf && double.TryParse(pf, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed))
            {
                factor = Math.Clamp(parsed, 0.0, 1.0);
            }

            Color baseColor;
            if (value is SolidColorBrush scb)
            {
                baseColor = scb.Color;
            }
            else if (value is Color c)
            {
                baseColor = c;
            }
            else if (value is string s)
            {
                try
                {
                    baseColor = Color.Parse(s);
                }
                catch
                {
                    return value ?? Brushes.Transparent;
                }
            }
            else
            {
                return value ?? Brushes.Transparent;
            }

            byte lightenChannel(byte channel)
            {
                double v = channel + (255 - channel) * factor;
                return (byte)Math.Round(Math.Min(255, Math.Max(0, v)));
            }

            var newColor = new Color(baseColor.A, lightenChannel(baseColor.R), lightenChannel(baseColor.G), lightenChannel(baseColor.B));
            return new SolidColorBrush(newColor);
        }

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
        public enum MsgTypes
        {
            Info,
            Warning,
            Error
        }
        public enum PopupTypes
        {
            Message,
            Question
        }
    }
}
