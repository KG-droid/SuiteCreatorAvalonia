using Avalonia.Media;
using Newtonsoft.Json;
using SuiteCreatorAvalonia.Converters;
using SuiteCreatorAvalonia.Models.Common;
using SuiteCreatorAvalonia.Models.Package;
using SuiteCreatorAvalonia.Models.Rules;
using SuiteOperations.Events;

namespace SuiteOperations
{
    public class SuiteExecConfig
    {
        [JsonConstructor]
        public SuiteExecConfig() { }

        public string? ProjectName { get; set; }
        public List<PackageBase> Packages { get; set; } = new();
        public List<RuleSet> RuleSets { get; set; } = new();
        public List<CertExecEvent> CertEvents { get; set; } = new();
        public List<DriverExecEvent> DriverEvents { get; set; } = new();
        public List<ProcClosureExecEvent> ProcClosureEvents { get; set; } = new();
        public List<EnvExecEvent> EnvironmentEvents { get; set; } = new();
        public List<ExecutableExecEvent> ExecutableEvents { get; set; } = new();
        public List<BrowserExExecEvent> ExtensionEvents { get; set; } = new();
        public List<FileExecEvent> FileEvents { get; set; } = new();
        public List<PowerShellExecEvent> PowerShellEvents { get; set; } = new();
        public List<RegExecEvent> RegistryEvents { get; set; } = new();
        public List<ServiceClosureExecEvent> ServiceClosureEvents { get; set; } = new();
        public List<ShortcutExecEvent> ShortcutEvents { get; set; } = new();
        public List<Stage>? Stages { get; set; } = new();
        public Build BuildSettings { get; set; } = new();
        public Popup PopupSettings { get; set; } = new();

        // Baked in at build time from the admin's install-level AppSettings.json (see
        // SuiteBuilder.BuildSuiteExecConfig) so the built exe can evaluate it without needing
        // access to that file at runtime. Deliberately not part of Popup - that class is also the
        // project's own editable popup settings, and this is a machine-level override, not project data.
        public bool HasGlobalPSCondition { get; set; }
        public string? GlobalPSCondition { get; set; }

        // Same story as above: an admin-controlled setting from AppSettings.json (see
        // AppSettingsControl.GetCompanyLogoBackgroundColor), baked in at build time so the progress
        // popup can use it without needing that file - not project data, so it lives here rather than
        // on Popup, which is also the project's own editable popup settings.
        public Color CompanyLogoBackgroundColor { get; set; } = Color.Parse("#497cab");

        public void ToJson(string saveFilePath)
        {
            if (string.IsNullOrWhiteSpace(saveFilePath))
            {
                throw new ArgumentException("Cannot export suite config a Json, no file path was provided", nameof(saveFilePath));
            }

            string json = JsonConvert.SerializeObject(this, typeof(SuiteExecConfig), Formatting.Indented, CreateJsonSerializerSettings());
            File.WriteAllText(saveFilePath, json);
        }

        public SuiteExecConfig Clone()
        {
            string json = JsonConvert.SerializeObject(this, typeof(SuiteExecConfig), Formatting.None, CreateJsonSerializerSettings());
            SuiteExecConfig? config = JsonConvert.DeserializeObject<SuiteExecConfig>(json, CreateJsonSerializerSettings());

            if (config != null)
            {
                return config;
            }

            throw new Exception("The provided Suite exec config file is invalid");
        }

        public SuiteExecConfig FromJson(string jsonPath)
        {
            SuiteExecConfig? config = JsonConvert.DeserializeObject<SuiteExecConfig>(File.ReadAllText(jsonPath), CreateJsonSerializerSettings());

            if (config != null)
            {
                return config;
            }

            throw new Exception("The provided Suite exec config file is invalid");
        }

        private static JsonSerializerSettings CreateJsonSerializerSettings()
        {
            return new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                Converters =
                {
                    new ColorToJson(),
                    new BitmapToJson(),
                    new TextDocumentToJson(),
                },
                MaxDepth = null,
            };
        }
    }
}
