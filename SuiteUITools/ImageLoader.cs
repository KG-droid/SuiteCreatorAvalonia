using Avalonia.Platform;
using Bitmap = Avalonia.Media.Imaging.Bitmap;

namespace SuiteCreatorAvalonia.Tools
{
    public class ImageLoader
    {
        public static Bitmap? Get(Uri imageUri)
        {
            if (imageUri.IsFile || Path.IsPathRooted(imageUri.OriginalString))
            {
                string filePath = imageUri.IsFile ? imageUri.LocalPath : imageUri.OriginalString;
                if (File.Exists(filePath))
                {
                    using (Stream stream = File.OpenRead(filePath))
                        return new Bitmap(stream);
                }
            }
            if (AssetLoader.Exists(imageUri))
            {
                using (Stream stream = AssetLoader.Open(imageUri))
                    return new Bitmap(stream);
            }
            return null;
        }

        public static byte[]? GetBytes(Uri imageUri)
        {
            if (imageUri.IsFile || Path.IsPathRooted(imageUri.OriginalString))
            {
                string filePath = imageUri.IsFile ? imageUri.LocalPath : imageUri.OriginalString;
                if (File.Exists(filePath))
                {
                    using (FileStream fileStream = File.OpenRead(filePath))
                    {
                        var memoryStream = new MemoryStream();
                        fileStream.CopyTo(memoryStream);
                        return memoryStream.ToArray();
                    }
                }
            }
            if (AssetLoader.Exists(imageUri))
            {
                using (Stream imgStream = AssetLoader.Open(imageUri))
                {
                    var memoryStream = new MemoryStream();
                    imgStream.CopyTo(memoryStream);
                    return memoryStream.ToArray();
                }
            }
            return null;
        }

        public static Bitmap? GetFromBase64(string? base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
                return null;

            try
            {
                var trimmed = base64.Trim();

                // Supports data URIs: data:image/png;base64,xxxx
                var commaIndex = trimmed.IndexOf(',');
                if (commaIndex >= 0 && trimmed[..commaIndex].Contains("base64", StringComparison.OrdinalIgnoreCase))
                    trimmed = trimmed[(commaIndex + 1)..];

                var bytes = Convert.FromBase64String(trimmed);
                using var ms = new MemoryStream(bytes);
                return new Bitmap(ms);
            }
            catch
            {
                return null;
            }
        }

        public static string? GetBase64(Bitmap? bitmap)
        {
            if (bitmap is null)
                return null;

            using var ms = new MemoryStream();
            bitmap.Save(ms);
            return Convert.ToBase64String(ms.ToArray());
        }
    }
}

