using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using System.ComponentModel;

namespace SuiteCreatorAvalonia.Models.Package
{
    public class MSIProperty : INotifyPropertyChanged
    {
        private string? _name;
        public string? Name
        {
            get => _name;
            set
            {
                if (_name == value) return;
                _name = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Name)));
            }
        }

        private string? _value;
        public string? Value
        {
            get => _value;
            set
            {
                if (_value == value) return;
                _value = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Value)));
            }
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public MSIProperty Clone()
        {
            return new MSIProperty
            {
                Name = Name,
                Value = Value,
            };
        }
        public void UpdateFrom(MSIProperty property)
        {
            Name = property.Name;
            Value = property.Value;
        }
    }

    public class MSI : PackageBase
    {
        public bool SecureTransforms { get; set; }
        public Contexts? Context { get; set; }
        public bool IsCreateLog { get; set; }
        public bool IsRemoveFamily { get; set; }
        public bool LegacyLongFilePath { get; set; } = false;
        public RestartBehavior RestartBehavior { get; set; } = RestartBehavior.Ignore;
        public string? LogPath { get; set; }
        public List<MSIProperty>? Properties { get; set; }
        public bool RemoveOnSuiteRemoval { get; set; } = true;
        public MSI? Rollback { get; set; }

        protected MSI(MSI source) : base()
        {
            Id = source.Id;
            Name = source.Name;
            RequirementRuleSetId = source.RequirementRuleSetId;
            SecureTransforms = source.SecureTransforms;
            Context = source.Context;
            IsCreateLog = source.IsCreateLog;
            IsRemoveFamily = source.IsRemoveFamily;
            LegacyLongFilePath = source.LegacyLongFilePath;
            LogPath = source.LogPath;
            RestartBehavior = source.RestartBehavior;
            Properties = source.Properties != null ? source.Properties.Select(p => p.Clone()).ToList() : null;
            RemoveOnSuiteRemoval = source.RemoveOnSuiteRemoval;
            Rollback = source.Rollback?.Clone();
        }

        public MSI() : base() { }

        public override MSI Clone()
        {
            return new MSI(this);
        }
        public override void UpdateFrom(Stage stage)
        {
            if (stage == null) return;
            if (stage is MSI msi)
            {
                Id = msi.Id;
                Name = msi.Name;
                RequirementRuleSetId = msi.RequirementRuleSetId;
                SecureTransforms = msi.SecureTransforms;
                Context = msi.Context;
                IsCreateLog = msi.IsCreateLog;
                IsRemoveFamily = msi.IsRemoveFamily;
                LegacyLongFilePath = msi.LegacyLongFilePath;
                LogPath = msi.LogPath;
                RestartBehavior = msi.RestartBehavior;
                Properties = msi.Properties != null ? msi.Properties.Select(p => p.Clone()).ToList() : null;
                RemoveOnSuiteRemoval = msi.RemoveOnSuiteRemoval;
                Rollback = msi.Rollback?.Clone();
            }
        }
        public override string? Validate()
        {
            if (Context == null || !Enum.IsDefined(typeof(Contexts), Context))
            {
                return "An install context is required.";
            }
            if (IsCreateLog && string.IsNullOrWhiteSpace(LogPath))
            {
                return "Log Path is required when logging is enabled.";
            }
            if (IsCreateLog && !string.IsNullOrWhiteSpace(LogPath) && !Path.IsPathFullyQualified(LogPath))
            {
                return "Log Path must be a fully qualified path.";
            }
            if (Properties != null)
            {
                foreach (var property in Properties)
                {
                    if (string.IsNullOrWhiteSpace(property.Name))
                    {
                        return "Property Name cannot be empty.";
                    }
                }
            }
            if (Rollback != null)
            {
                var rollbackValidation = Rollback.Validate();
                if (rollbackValidation != null)
                {
                    return "Package Rollback:" + rollbackValidation;
                }
            }
            return null;
        }
    }

    public partial class MSIInstallRemovalItem
    {
        public bool IsProductRemoval { get; set; }

        public string? Code { get; set; }

        public string? LogPath { get; set; }

        public string? AdditionalParams { get; set; }

        public MSIInstallRemovalItem Clone()
        {
            return new MSIInstallRemovalItem
            {
                IsProductRemoval = IsProductRemoval,
                Code = Code,
                LogPath = LogPath,
                AdditionalParams = AdditionalParams
            };
        }

        public void UpdateFrom(MSIInstallRemovalItem remItem)
        {
            if (remItem == null) return;
            IsProductRemoval = remItem.IsProductRemoval;
            Code = remItem.Code;
            LogPath = remItem.LogPath;
            AdditionalParams = remItem.AdditionalParams;
        }
    }

}
