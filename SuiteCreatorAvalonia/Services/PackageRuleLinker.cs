using SuiteCreatorAvalonia.Models.Rules;
using SuiteCreatorAvalonia.ViewModels;
using System.Linq;
using System.Threading.Tasks;

namespace SuiteCreatorAvalonia.Services
{
    /// <summary>
    /// Resolves the RuleSet a package's requirement/detection rule should use. If an orphaned rule
    /// (no longer owned by any package, but still kept around because an event uses it) already has
    /// the package's name, offers to link it instead of silently letting a new same-named rule collide with it.
    /// </summary>
    internal static class PackageRuleLinker
    {
        /// <summary>
        /// Resolves the RuleSet, and reports whether it was an existing rule that got linked (its
        /// conditions should be loaded into the UI as-is) versus a fresh, empty one the caller should populate.
        /// </summary>
        public static async Task<(RuleSet RuleSet, bool WasLinked)> ResolvePackageRuleSetAsync(ViewModelBase owner, SuiteCoreManager suiteCoreManager, string packageName, string suffix)
        {
            RuleSet? orphan = suiteCoreManager.FindUnlinkedRuleSetByName(packageName);
            if (orphan != null)
            {
                DialogHostViewModel linkPrompt = new DialogHostViewModel(
                    "Existing Rule Found",
                    $"A Rule already exists in the suite for \"{packageName}\". Do you want to link that existing rule?",
                    DialogHostViewModel.MsgTypes.Info,
                    DialogHostViewModel.PopupTypes.Question,
                    "Link Existing Rule",
                    "No, Rename It",
                    false,
                    null,
                    null);
                object? linkResult = await owner.ShowDialogAsync(linkPrompt);
                if (linkResult is string choice && choice == "Link Existing Rule")
                {
                    suiteCoreManager.RenameRuleSet(orphan.Id, packageName + suffix);
                    return (suiteCoreManager.GetRuleSet(orphan.Id), true);
                }

                string[] takenNames = suiteCoreManager.GetRuleSets()
                    .Where(r => r.Id != orphan.Id && !string.IsNullOrWhiteSpace(r.Name))
                    .Select(r => r.Name!)
                    .ToArray();
                RenameExistingRuleViewModel renameVm = new RenameExistingRuleViewModel(orphan.Name ?? packageName, takenNames);
                object? renameResult = await owner.ShowDialogAsync(renameVm);
                string newName = renameResult as string ?? MakeUniqueFallbackName(orphan.Name ?? packageName, takenNames);
                suiteCoreManager.RenameRuleSet(orphan.Id, newName);
            }

            return (suiteCoreManager.NewRuleSet(packageName + suffix), false);
        }

        private static string MakeUniqueFallbackName(string baseName, string[] takenNames)
        {
            string candidate = $"{baseName} (Renamed)";
            int suffixNum = 2;
            while (takenNames.Any(n => n.Equals(candidate, System.StringComparison.OrdinalIgnoreCase)))
            {
                candidate = $"{baseName} (Renamed {suffixNum})";
                suffixNum++;
            }
            return candidate;
        }
    }
}
