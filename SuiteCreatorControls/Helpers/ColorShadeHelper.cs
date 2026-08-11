using Avalonia.Media;
using System;

namespace SuiteCreatorControls.Helpers
{
    public static class ColorShadeHelper
    {
        // Boosts a colour's saturation while keeping its hue and brightness (HSV value)
        // unchanged, so a washed-out/pale colour turns into a deeper, more vivid tone of
        // the same colour rather than fading towards grey or black.
        public static Color GetVividShade(Color color, double saturationBoost = 0.6)
        {
            var (h, s, v) = ToHsv(color);

            var newS = Math.Clamp(s + (1.0 - s) * saturationBoost, 0.0, 1.0);

            return FromHsv(color.A, h, newS, v);
        }

        private static (double H, double S, double V) ToHsv(Color color)
        {
            double r = color.R / 255.0;
            double g = color.G / 255.0;
            double b = color.B / 255.0;

            double max = Math.Max(r, Math.Max(g, b));
            double min = Math.Min(r, Math.Min(g, b));
            double delta = max - min;

            double v = max;
            double s = max == 0 ? 0.0 : delta / max;

            if (delta == 0)
                return (0.0, s, v);

            double h;
            if (max == r)
                h = ((g - b) / delta) % 6.0;
            else if (max == g)
                h = ((b - r) / delta) + 2.0;
            else
                h = ((r - g) / delta) + 4.0;

            h *= 60.0;
            if (h < 0)
                h += 360.0;

            return (h, s, v);
        }

        private static Color FromHsv(byte alpha, double h, double s, double v)
        {
            double c = v * s;
            double x = c * (1.0 - Math.Abs((h / 60.0) % 2.0 - 1.0));
            double m = v - c;

            double r1, g1, b1;
            if (h < 60) { r1 = c; g1 = x; b1 = 0; }
            else if (h < 120) { r1 = x; g1 = c; b1 = 0; }
            else if (h < 180) { r1 = 0; g1 = c; b1 = x; }
            else if (h < 240) { r1 = 0; g1 = x; b1 = c; }
            else if (h < 300) { r1 = x; g1 = 0; b1 = c; }
            else { r1 = c; g1 = 0; b1 = x; }

            byte r = (byte)Math.Round((r1 + m) * 255.0);
            byte g = (byte)Math.Round((g1 + m) * 255.0);
            byte b = (byte)Math.Round((b1 + m) * 255.0);

            return Color.FromArgb(alpha, r, g, b);
        }
    }
}
