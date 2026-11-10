using SuiteCreatorAvalonia.Enums;

namespace SuiteCreatorAvalonia.Models.Events
{
    public class SystemFont : EventCoreWithPermanence
    {
        public static readonly string[] FontExtensions = { ".ttf", ".otf", ".ttc", ".otc" };
        private static readonly char[] FileNameSeparators = { ',', ';' };

        public required FontAction Action { get; set; }

        // Install: either a single font file, or a folder, in which case every font file in it is installed.
        public string? FontPath { get; set; }

        // Windows registers installed fonts by file name (in the Fonts folder), so removal identifies the
        // font(s) by file name. A comma separated list is allowed so a whole family can be removed in one event.
        public string? FontFileName { get; set; }

        public static bool IsFontFile(string path)
        {
            return FontExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase);
        }

        public IEnumerable<string> GetFileNames()
        {
            if (string.IsNullOrWhiteSpace(FontFileName)) return Enumerable.Empty<string>();
            return FontFileName.Split(FileNameSeparators, StringSplitOptions.RemoveEmptyEntries)
                .Select(n => n.Trim())
                .Where(n => n.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase);
        }

        // The font file name(s) an Install of FontPath will put in the Fonts folder, used so the event can be
        // reversed into a Remove. Returns null if FontPath isn't set or can't be read.
        public static string? DeriveFileNames(string? fontPath)
        {
            if (string.IsNullOrWhiteSpace(fontPath)) return null;
            if (Directory.Exists(fontPath))
            {
                List<string> names = Directory.EnumerateFiles(fontPath, "*", SearchOption.AllDirectories)
                    .Where(IsFontFile)
                    .Select(Path.GetFileName)
                    .Where(n => n != null)
                    .Select(n => n!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                return names.Count > 0 ? string.Join(", ", names) : null;
            }
            return Path.GetFileName(fontPath);
        }

        public override SystemFont Clone()
        {
            return new SystemFont
            {
                Id = Id,
                Action = Action,
                FontPath = FontPath,
                FontFileName = FontFileName,
                Schedules = Schedules.ConvertAll(s => s.Clone()),
                IsPermanent = IsPermanent,
            };
        }

        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not SystemFont font) return;
            Id = font.Id;
            Action = font.Action;
            FontPath = font.FontPath;
            FontFileName = font.FontFileName;
            Schedules = font.Schedules.ConvertAll(s => s.Clone());
            IsPermanent = font.IsPermanent;
        }

        public override string? Validate()
        {
            if (!System.Enum.IsDefined(typeof(FontAction), Action))
            {
                return "Action must be specified for Font event.";
            }
            if (Action == FontAction.Install)
            {
                if (string.IsNullOrWhiteSpace(FontPath))
                {
                    return "A font file or folder path must be specified for Font Install event.";
                }
                if (Directory.Exists(FontPath))
                {
                    if (!Directory.EnumerateFiles(FontPath, "*", SearchOption.AllDirectories).Any(IsFontFile))
                    {
                        return $"Font folder: {FontPath}, does not contain any font files ({string.Join(", ", FontExtensions)})";
                    }
                }
                else
                {
                    if (!IsFontFile(FontPath))
                    {
                        return $"Font file path: {FontPath}, must point to a font file ({string.Join(", ", FontExtensions)}) or a folder of them";
                    }
                    if (!File.Exists(FontPath))
                    {
                        return $"Font file path: {FontPath}, does not exist";
                    }
                }
            }
            if (Action == FontAction.Remove)
            {
                if (!GetFileNames().Any())
                {
                    return "At least one font file name must be specified for Font Remove event.";
                }
                if (GetFileNames().Any(n => !IsFontFile(n) || n != Path.GetFileName(n)))
                {
                    return $"Font file names for removal must be plain file names ending in {string.Join(", ", FontExtensions)}, e.g. myfont.ttf";
                }
            }
            return null;
        }

        public override void Reverse()
        {
            if (IsPermanent) return;
            // Already a Remove: nothing to reverse, so leave the schedule alone (it should only run on the side it was configured for).
            if (Action == FontAction.Remove) return;
            base.Reverse();
            if (string.IsNullOrWhiteSpace(FontFileName))
            {
                FontFileName = DeriveFileNames(FontPath);
            }
            Action = FontAction.Remove;
        }
    }
}
