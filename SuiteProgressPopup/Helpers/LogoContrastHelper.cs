using Avalonia.Media;
using SuiteProgressPopup.Services;
using System;
using System.IO;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;
using DrawingBitmap = System.Drawing.Bitmap;
using DrawingColor = System.Drawing.Color;

namespace SuiteProgressPopup.Helpers
{
    // Decides whether a logo needs a contrast treatment against a given background, and generates
    // a soft ambient glow behind it (like the halo around the Windows Update spinner) rather than
    // a hard-edged outline ring.
    internal static class LogoContrastHelper
    {
        // Below this WCAG-style contrast ratio (1 = identical, 21 = black/white), the logo
        // is considered too close to the background colour to read clearly on its own.
        private const double LowContrastThreshold = 2.5;

        private const int GlowRadiusPx = 28;
        private const byte GlowMaxAlpha = 220;
        private const int WorkingMaxDimension = 96;
        private const int OpaqueAlphaThreshold = 64;

        public static bool NeedsGlow(string logoFilePath, Color backgroundColour)
        {
            Color? logoColour = ComputeAverageColour(logoFilePath);
            return logoColour is null || GetContrastRatio(logoColour.Value, backgroundColour) < LowContrastThreshold;
        }

        // Returns the logo with a soft ambient glow baked in behind its silhouette, on a canvas
        // padded by the glow radius. The padding guarantees there's always room to draw the glow
        // even when the source logo is a fully opaque shape (e.g. a plain rectangle) with no
        // transparent margin of its own to draw into.
        public static AvaloniaBitmap? CreateLogoWithGlow(string logoFilePath, Color glowColour)
        {
            if (!File.Exists(logoFilePath))
                return null;

            try
            {
                using DrawingBitmap source = new DrawingBitmap(logoFilePath);

                double scale = Math.Min(1.0, (double)WorkingMaxDimension / Math.Max(source.Width, source.Height));
                int workW = Math.Max(1, (int)Math.Round(source.Width * scale));
                int workH = Math.Max(1, (int)Math.Round(source.Height * scale));

                using DrawingBitmap resized = new DrawingBitmap(workW, workH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(resized))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(source, 0, 0, workW, workH);
                }

                bool[,] opaque = new bool[workW, workH];
                for (int y = 0; y < workH; y++)
                {
                    for (int x = 0; x < workW; x++)
                    {
                        opaque[x, y] = resized.GetPixel(x, y).A >= OpaqueAlphaThreshold;
                    }
                }

                int paddedW = workW + GlowRadiusPx * 2;
                int paddedH = workH + GlowRadiusPx * 2;

                using DrawingBitmap composite = new DrawingBitmap(paddedW, paddedH, System.Drawing.Imaging.PixelFormat.Format32bppArgb);

                for (int py = -GlowRadiusPx; py < workH + GlowRadiusPx; py++)
                {
                    for (int px = -GlowRadiusPx; px < workW + GlowRadiusPx; px++)
                    {
                        if (IsOpaque(opaque, workW, workH, px, py))
                            continue;

                        double nearestDistance = FindNearestOpaqueDistance(opaque, workW, workH, px, py);
                        if (nearestDistance > GlowRadiusPx)
                            continue;

                        // Quadratic ease-out falloff reads as a soft ambient glow rather than the
                        // hard, evenly-graded ring a linear falloff would give.
                        double t = 1.0 - nearestDistance / GlowRadiusPx;
                        byte alpha = (byte)Math.Clamp(GlowMaxAlpha * t * t, 0, 255);
                        composite.SetPixel(px + GlowRadiusPx, py + GlowRadiusPx,
                            DrawingColor.FromArgb(alpha, glowColour.R, glowColour.G, glowColour.B));
                    }
                }

                // Draw the real logo on top, centred within the padded canvas, so it covers
                // the part of the glow directly beneath it and only the surrounding halo shows.
                using (System.Drawing.Graphics g = System.Drawing.Graphics.FromImage(composite))
                {
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.DrawImage(resized, GlowRadiusPx, GlowRadiusPx, workW, workH);
                }

                using MemoryStream ms = new MemoryStream();
                composite.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                ms.Seek(0, SeekOrigin.Begin);
                return new AvaloniaBitmap(ms);
            }
            catch (Exception ex)
            {
                AppLogService.Warning($"Failed to generate logo glow: {ex.Message}", nameof(LogoContrastHelper));
                return null;
            }
        }

        private static bool IsOpaque(bool[,] opaque, int width, int height, int x, int y)
            => x >= 0 && x < width && y >= 0 && y < height && opaque[x, y];

        private static double FindNearestOpaqueDistance(bool[,] opaque, int width, int height, int x, int y)
        {
            double nearest = double.MaxValue;
            for (int dy = -GlowRadiusPx; dy <= GlowRadiusPx; dy++)
            {
                for (int dx = -GlowRadiusPx; dx <= GlowRadiusPx; dx++)
                {
                    if (!IsOpaque(opaque, width, height, x + dx, y + dy))
                        continue;

                    double dist = Math.Sqrt(dx * dx + dy * dy);
                    if (dist < nearest)
                        nearest = dist;
                }
            }

            return nearest;
        }

        private static Color? ComputeAverageColour(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            try
            {
                using DrawingBitmap drawingBitmap = new DrawingBitmap(filePath);
                long a = 0, r = 0, g = 0, b = 0;
                int stepX = Math.Max(1, drawingBitmap.Width / 64);
                int stepY = Math.Max(1, drawingBitmap.Height / 64);

                for (int y = 0; y < drawingBitmap.Height; y += stepY)
                {
                    for (int x = 0; x < drawingBitmap.Width; x += stepX)
                    {
                        DrawingColor pixel = drawingBitmap.GetPixel(x, y);
                        if (pixel.A < 32)
                            continue;

                        a += pixel.A;
                        r += pixel.R * pixel.A;
                        g += pixel.G * pixel.A;
                        b += pixel.B * pixel.A;
                    }
                }

                if (a == 0)
                    return null;

                return Color.FromRgb((byte)(r / a), (byte)(g / a), (byte)(b / a));
            }
            catch (Exception ex)
            {
                AppLogService.Warning($"Failed to compute average logo colour for contrast check: {ex.Message}", nameof(LogoContrastHelper));
                return null;
            }
        }

        private static double GetContrastRatio(Color a, Color b)
        {
            double lumA = GetRelativeLuminance(a) + 0.05;
            double lumB = GetRelativeLuminance(b) + 0.05;
            return lumA > lumB ? lumA / lumB : lumB / lumA;
        }

        private static double GetRelativeLuminance(Color colour)
        {
            static double Linearize(byte channel)
            {
                double v = channel / 255.0;
                return v <= 0.03928 ? v / 12.92 : Math.Pow((v + 0.055) / 1.055, 2.4);
            }

            return 0.2126 * Linearize(colour.R) + 0.7152 * Linearize(colour.G) + 0.0722 * Linearize(colour.B);
        }
    }
}
