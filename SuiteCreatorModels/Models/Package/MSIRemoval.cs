using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using Architecture = System.Runtime.InteropServices.Architecture;
using Contexts = SuiteCreatorAvalonia.Enums.Contexts;

namespace SuiteCreatorAvalonia.Models.Package
{
    public class MSIRemoval : PackageBase
    {
        public string? RemovalCode { get; set; }
        public bool IsProductRemoval { get; set; }
        public bool IsCreateLog { get; set; }
        public string? LogPath { get; set; }
        public Architecture Architecture { get; set; }
        public Contexts? Context { get; set; }
        public RestartBehavior RestartBehavior { get; set; } = RestartBehavior.Ignore;
        public List<MSIProperty>? Properties { get; set; }

        public override MSIRemoval Clone()
        {
            return new MSIRemoval
            {
                Id = Id,
                Name = Name,
                RemovalCode = RemovalCode,
                IsProductRemoval = IsProductRemoval,
                IsCreateLog = IsCreateLog,
                LogPath = LogPath,
                RequirementRuleSetId = RequirementRuleSetId,
                Context = Context,
                Architecture = Architecture,
                RestartBehavior = RestartBehavior,
                Properties = Properties != null ? Properties.Select(p => p.Clone()).ToList() : null
            };
        }

        public override void UpdateFrom(Stage stage)
        {
            if (stage == null) return;
            if (stage is MSIRemoval msirem)
            {
                Id = msirem.Id;
                Name = msirem.Name;
                RemovalCode = msirem.RemovalCode;
                IsProductRemoval = msirem.IsProductRemoval;
                IsCreateLog = msirem.IsCreateLog;
                LogPath = msirem.LogPath;
                RequirementRuleSetId = msirem.RequirementRuleSetId;
                Context = msirem.Context;
                Architecture = msirem.Architecture;
                RestartBehavior = msirem.RestartBehavior;
                Properties = msirem.Properties != null ? msirem.Properties.Select(p => p.Clone()).ToList() : null;
            }
        }

        public override string? Validate()
        {
            if (string.IsNullOrWhiteSpace(RemovalCode))
            {
                return "Removal code is null. An Upgrade, or Product code is required (Determined by removal type).";
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
                foreach (MSIProperty property in Properties)
                {
                    if (string.IsNullOrWhiteSpace(property.Name))
                        return "Property Name cannot be empty.";
                }
            }
            return null;
        }
    }
}
