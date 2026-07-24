using System.Security.AccessControl;

namespace SuiteCreatorAvalonia.Models.Common
{
    public class Permission
    {
        public PermissionGroup? UserGroup { get; set; }
        public AccessControlType? PermissionType { get; set; }
        public List<FileSystemRights> PermissionRights { get; set; } = new();

        public Permission Clone()
        {
            return new Permission
            {
                UserGroup = UserGroup != null ? UserGroup?.Clone() : null,
                PermissionType = PermissionType,
                PermissionRights = PermissionRights != null
                    ? new(PermissionRights)
                    : new(),
            };
        }

        public Permission UpdateFrom(Permission permission)
        {
            if (permission == null)
            {
                return this;
            }
            UserGroup = permission.UserGroup;
            PermissionType = permission.PermissionType;
            PermissionRights.Clear();
            PermissionRights.AddRange(permission.PermissionRights);
            return this;
        }
    }
}
