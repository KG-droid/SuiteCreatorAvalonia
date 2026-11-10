using System.Text;

namespace SuiteOperations.Events
{
    /// <summary>
    /// Reads the name Windows registers a font under (the "Arial (TrueType)" style value name) straight out of
    /// the font file's sfnt 'name' table, so no font library is needed. Handles .ttf/.otf and .ttc/.otc collections.
    /// </summary>
    internal static class FontNameReader
    {
        private const ushort FullFontNameId = 4;
        private const ushort WindowsPlatformId = 3;
        private const ushort MacPlatformId = 1;
        private const ushort UnicodePlatformId = 0;
        private const ushort EnglishUsLanguageId = 0x0409;

        public static string GetRegistryName(string fontPath)
        {
            byte[] data = File.ReadAllBytes(fontPath);
            List<uint> fontOffsets = new();
            if (data.Length >= 12 && ReadTag(data, 0) == "ttcf")
            {
                uint fontCount = ReadUInt32(data, 8);
                for (int i = 0; i < fontCount; i++)
                {
                    fontOffsets.Add(ReadUInt32(data, 12 + (i * 4)));
                }
            }
            else
            {
                fontOffsets.Add(0);
            }

            List<string> names = new();
            bool isOpenType = false;
            foreach (uint fontOffset in fontOffsets)
            {
                string? name = ReadFullName(data, (int)fontOffset, out bool isCff);
                if (!string.IsNullOrWhiteSpace(name) && !names.Contains(name, StringComparer.OrdinalIgnoreCase))
                    names.Add(name);
                isOpenType |= isCff;
            }
            if (names.Count == 0)
            {
                throw new InvalidDataException($"Could not read a font name from: {fontPath}");
            }
            return $"{string.Join(" & ", names)} ({(isOpenType ? "OpenType" : "TrueType")})";
        }

        private static string? ReadFullName(byte[] data, int fontOffset, out bool isCff)
        {
            isCff = ReadTag(data, fontOffset) == "OTTO";
            int tableCount = ReadUInt16(data, fontOffset + 4);
            for (int i = 0; i < tableCount; i++)
            {
                int record = fontOffset + 12 + (i * 16);
                if (ReadTag(data, record) != "name") continue;

                int tableOffset = (int)ReadUInt32(data, record + 8);
                int recordCount = ReadUInt16(data, tableOffset + 2);
                int stringsOffset = tableOffset + ReadUInt16(data, tableOffset + 4);

                string? fallback = null;
                for (int n = 0; n < recordCount; n++)
                {
                    int nameRecord = tableOffset + 6 + (n * 12);
                    if (ReadUInt16(data, nameRecord + 6) != FullFontNameId) continue;

                    ushort platformId = ReadUInt16(data, nameRecord);
                    ushort languageId = ReadUInt16(data, nameRecord + 4);
                    int length = ReadUInt16(data, nameRecord + 8);
                    int offset = stringsOffset + ReadUInt16(data, nameRecord + 10);

                    if (platformId == WindowsPlatformId || platformId == UnicodePlatformId)
                    {
                        string name = Encoding.BigEndianUnicode.GetString(data, offset, length);
                        if (platformId == WindowsPlatformId && languageId == EnglishUsLanguageId) return name;
                        fallback ??= name;
                    }
                    else if (platformId == MacPlatformId)
                    {
                        fallback ??= Encoding.Latin1.GetString(data, offset, length);
                    }
                }
                return fallback;
            }
            return null;
        }

        private static string ReadTag(byte[] data, int offset) => Encoding.ASCII.GetString(data, offset, 4);

        private static ushort ReadUInt16(byte[] data, int offset) => (ushort)((data[offset] << 8) | data[offset + 1]);

        private static uint ReadUInt32(byte[] data, int offset) =>
            ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
    }
}
