using System.IO;
using System.Text.Json;
using SuiteUserPopup.Models.Config;

namespace SuiteUserPopup.Services;

public static class popConfigLoader
{
    public static PopConfig? LoadFromJsonFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath) || !File.Exists(filePath))
            return null;

        var json = File.ReadAllText(filePath);
        return JsonSerializer.Deserialize(json, PopConfigJsonContext.Default.PopConfig);
    }
}
