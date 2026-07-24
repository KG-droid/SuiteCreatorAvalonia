using System.Drawing;
using System.Drawing.Imaging;
using System.IO;

namespace SuiteCreatorAvalonia.Converters
{
    public class BitmapConverter
    {
        public static Bitmap InvertColors(Bitmap original)
        {
            // Clone the original bitmap to avoid modifying the original directly
            Bitmap inverted = new Bitmap(original.Width, original.Height);

            for (int y = 0; y < original.Height; y++)
            {
                for (int x = 0; x < original.Width; x++)
                {
                    Color pixelColor = original.GetPixel(x, y);
                    Color invertedColor = Color.FromArgb(
                        pixelColor.A, // Preserve alpha
                        255 - pixelColor.R,
                        255 - pixelColor.G,
                        255 - pixelColor.B
                    );
                    inverted.SetPixel(x, y, invertedColor);
                }
            }

            return inverted;
        }

        public static Bitmap AvaloniaBitmapToDrawingBitmap(Avalonia.Media.Imaging.Bitmap avaloniaBitmap)
        {
            using (var ms = new MemoryStream())
            {
                avaloniaBitmap.Save(ms);
                ms.Seek(0, SeekOrigin.Begin);
                return new Bitmap(ms);
            }
        }

        public static Avalonia.Media.Imaging.Bitmap DrawingBitmapToAvaloniaBitmap(Bitmap drawingBitmap)
        {
            using (var ms = new MemoryStream())
            {
                drawingBitmap.Save(ms, ImageFormat.Png);
                ms.Seek(0, SeekOrigin.Begin);
                return new Avalonia.Media.Imaging.Bitmap(ms);
            }
        }
    }
}

