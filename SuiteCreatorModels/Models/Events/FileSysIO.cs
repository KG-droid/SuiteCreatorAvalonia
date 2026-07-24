using SuiteCreatorAvalonia.Enums;
using SuiteCreatorAvalonia.Models.Common;
using SpecialFolder = System.Environment.SpecialFolder;

namespace SuiteCreatorAvalonia.Models.Events
{
    public class FileSysIO : EventCoreWithPermanence
    {
        public FileSysIOAction Action { get; set; }
        public FileSysIOType FileSysIOType { get; set; }
        public List<VariableText>? SourcePath { get; set; }
        public List<VariableText>? DestinationPath { get; set; }
        public bool ReplaceExisting { get; set; }
        public bool OverridePermissions { get; set; }
        public List<Permission>? Permissions { get; set; }
        public string? TargetsName { get; set; }

        bool IsUserProfileOrSubfolder(SpecialFolder folder)
        {
            var userProfileFolders = new HashSet<SpecialFolder>
            {
                SpecialFolder.UserProfile,
                SpecialFolder.Desktop,
                SpecialFolder.MyDocuments,
                SpecialFolder.MyPictures,
                SpecialFolder.MyMusic,
                SpecialFolder.MyVideos,
                SpecialFolder.Favorites,
                SpecialFolder.ApplicationData,
                SpecialFolder.LocalApplicationData,
                SpecialFolder.Cookies,
                SpecialFolder.Recent,
                SpecialFolder.SendTo,
                SpecialFolder.StartMenu,
                SpecialFolder.Startup,
                SpecialFolder.Templates
            };
            return userProfileFolders.Contains(folder);
        }

        public void ValidatePathStructure(List<VariableText>? path)
        {
            if (path != null)
            {
                if (path.First() is LiteralText ltxt && !string.IsNullOrWhiteSpace(ltxt.Value) && path.Count > 1)
                {
                    throw new Exception("You can't have literal text before a variable in a FileEvent path");
                }
                if (path.Count(v => v is not LiteralText) > 1)
                {
                    throw new Exception("You can't have more than one Varaiable in a path in a FileEvent");
                }
                if (path.Count(v => v is LiteralText) > 2)
                {
                    throw new Exception("You can't have more than one literal text in a path in a FileEvent");
                }
            }
        }

        public void ValidatePathsStructures()
        {
            ValidatePathStructure(SourcePath);
            if (Action == FileSysIOAction.Copy || Action == FileSysIOAction.Deploy || Action == FileSysIOAction.Move)
                ValidatePathStructure(DestinationPath);
            if (
                (
                    Action == FileSysIOAction.Deploy ||
                    Action == FileSysIOAction.Copy ||
                    Action == FileSysIOAction.Move
                ) && SourcePath != null &&
                SourcePath.Where(s => s is SpecialDIR).Any(s => IsUserProfileOrSubfolder((SpecialFolder)((SpecialDIR)s).Value))
            )
            {
                throw new Exception("A Deploy, Copy, or Move FileEvent cannot have a User based variable as its source");
            }
        }

        public override FileSysIO Clone()
        {
            return new FileSysIO
            {
                Id = Id,
                Action = Action,
                FileSysIOType = FileSysIOType,
                ReplaceExisting = ReplaceExisting,
                SourcePath = SourcePath?.ConvertAll(s => s.Clone()),
                DestinationPath = DestinationPath?.ConvertAll(d => d.Clone()),
                OverridePermissions = OverridePermissions,
                Permissions = Permissions?.ConvertAll(p => p.Clone()),
                Schedules = Schedules.ConvertAll(s => s.Clone()),
                IsPermanent = IsPermanent,
            };
        }

        public override void UpdateFrom(EventCore ecore)
        {
            if (ecore is not FileSysIO file) return;
            Id = Id;
            Action = file.Action;
            FileSysIOType = file.FileSysIOType;
            SourcePath = file.SourcePath?.ConvertAll(s => s.Clone());
            DestinationPath = file.DestinationPath?.ConvertAll(d => d.Clone());
            OverridePermissions = file.OverridePermissions;
            ReplaceExisting = file.ReplaceExisting;
            Permissions = file.Permissions?.ConvertAll(p => p.Clone());
            Schedules = file.Schedules.ConvertAll(s => s.Clone());
        }

        public override string? Validate()
        {
            if (!System.Enum.IsDefined(typeof(FileSysIOAction), Action))
            {
                return "Action must be specified for FileSysIO event.";
            }
            if (!System.Enum.IsDefined(typeof(FileSysIOType), FileSysIOType))
            {
                return "FileSysIOType must be specified for FileSysIO event.";
            }
            if (SourcePath == null || SourcePath.Count == 0 || SourcePath.All(v => string.IsNullOrWhiteSpace(v.GetValue())))
            {
                return "SourcePath must be specified for FileSysIO event.";
            }
            if (
                (Action == FileSysIOAction.Copy ||
                Action == FileSysIOAction.Deploy ||
                Action == FileSysIOAction.Move) &&
                (DestinationPath == null || DestinationPath.Count == 0 || DestinationPath.All(v => string.IsNullOrWhiteSpace(v.GetValue())))
            )
            {
                return "DestinationPath must be specified for FileSysIO event when action is Copy, Deploy, or Move.";
            }
            try
            {
                ValidatePathsStructures();
            }
            catch (Exception ex)
            {
                return ex.Message;
            }
            return null;
        }

        public override void Reverse()
        {
            if (IsPermanent) return;
            // Already a Delete: nothing to reverse, so leave the schedule alone (it should only run on the side it was configured for).
            if (Action == FileSysIOAction.Delete) return;
            base.Reverse();
            Action = FileSysIOAction.Delete;
            string? parsedDestPath = DestinationPath != null ? string.Concat(DestinationPath.Select(d => d.GetValue())) : null;
            if (string.IsNullOrWhiteSpace(TargetsName)) throw new Exception("Failed to reverse FileSysIO event, as the TargetsName is null");
            SourcePath = new();
            SourcePath.Add(
                new LiteralText(
                    Path.Join(
                        Path.HasExtension(parsedDestPath) ? Path.GetDirectoryName(parsedDestPath) : parsedDestPath,
                        TargetsName
                    )
                )
            );
        }
    }
}
