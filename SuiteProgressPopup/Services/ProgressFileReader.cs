using SuiteProgressPopup.Models;
using System;
using System.IO;
using System.Text.Json;

namespace SuiteProgressPopup.Services;

public static class ProgressFileReader
{
    public static ProgressStatus? TryRead(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var reader = new StreamReader(stream);
            var json = reader.ReadToEnd();

            if (string.IsNullOrWhiteSpace(json))
                return null;

            return JsonSerializer.Deserialize(json, ProgressStatusJsonContext.Default.ProgressStatus);
        }
        catch (Exception)
        {
            // The executor may be mid-write; treat any read/parse failure as "no update this tick".
            return null;
        }
    }
}
