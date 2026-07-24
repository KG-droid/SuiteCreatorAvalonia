using System.Security.Principal;

namespace SuiteCreatorAvalonia.Models.Common
{
    public class PermissionGroup
    {
        public string? FriendlyName { get; set; }
        public SecurityIdentifier? SID { get; set; }

        public PermissionGroup Clone()
        {
            return new PermissionGroup
            {
                FriendlyName = FriendlyName,
                SID = SID == null ? null : new SecurityIdentifier(SID.Value.ToString())
            };
        }

        public PermissionGroup UpdateFrom(PermissionGroup group)
        {
            if (group == null)
            {
                return this;
            }
            FriendlyName = group.FriendlyName;
            SID = group.SID == null ? null : new SecurityIdentifier(group.SID.Value.ToString());
            return this;
        }
    }
}
