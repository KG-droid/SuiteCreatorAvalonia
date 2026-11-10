using Avalonia.Media;
using System;

namespace SuiteCreatorControls.Text
{
    /// <summary>
    /// The popup follows the end user's light/dark theme, so a custom text colour has to stay legible on both a
    /// near-white and a near-black card. Only mid-tone colours pass, which rules out black, white and pastels.
    /// </summary>
    public static class ReadableColors
    {
        private const double MinContrast = 3.0;
        private const double Margin = 0.005;

        // Luminance of the popup's dark card background (#161616) and of white, the two backgrounds to clear.
        private static readonly double DarkBackground = Luminance(Color.FromRgb(0x16, 0x16, 0x16));
        private static readonly double MinLuminance = MinContrast * (DarkBackground + 0.05) - 0.05;
        private static readonly double MaxLuminance = 1.05 / MinContrast - 0.05;

        public static bool IsReadable(Color color)
        {
            double luminance = Luminance(color);
            return luminance >= MinLuminance && luminance <= MaxLuminance;
        }

        /// <summary>Returns the colour unchanged if readable on both themes, otherwise the nearest colour that is, keeping its hue.</summary>
        public static Color Clamp(Color color)
        {
            double luminance = Luminance(color);
            if (luminance < MinLuminance)
                return Blend(color, Colors.White, MinLuminance + Margin);
            if (luminance > MaxLuminance)
                return Blend(color, Colors.Black, MaxLuminance - Margin);
            return color;
        }

        private static Color Blend(Color from, Color to, double targetLuminance)
        {
            double low = 0;
            double high = 1;
            for (int i = 0; i < 24; i++)
            {
                double mid = (low + high) / 2;
                double luminance = Luminance(Mix(from, to, mid));
                bool reached = to == Colors.White ? luminance >= targetLuminance : luminance <= targetLuminance;
                if (reached)
                    high = mid;
                else
                    low = mid;
            }
            return Mix(from, to, high);
        }

        private static Color Mix(Color from, Color to, double t)
        {
            byte Lerp(byte a, byte b) => (byte)Math.Round(a + (b - a) * t);
            return Color.FromRgb(Lerp(from.R, to.R), Lerp(from.G, to.G), Lerp(from.B, to.B));
        }

        private static double Luminance(Color color)
        {
            static double Linear(byte channel)
            {
                double c = channel / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
            return 0.2126 * Linear(color.R) + 0.7152 * Linear(color.G) + 0.0722 * Linear(color.B);
        }
    }
}
