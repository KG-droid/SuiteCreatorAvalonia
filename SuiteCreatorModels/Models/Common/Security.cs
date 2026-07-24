using System.Security.Principal;

namespace SuiteCreatorAvalonia.Models.Common
{
    public class Security
    {
        private static IEnumerable<SecurityIdentifier> GroupSids = new List<SecurityIdentifier>()
            {
                new SecurityIdentifier("S-1-5-11"), // Authenticated Users
                new SecurityIdentifier("S-1-5-18"), // Local System  
                new SecurityIdentifier("S-1-5-32-545"), // Users
                new SecurityIdentifier("S-1-5-32-544"), // Administrators
            };
        public static IEnumerable<PermissionGroup> GetPermissionGroups()
        {
            List<PermissionGroup> friendlySIDs = new List<PermissionGroup>();
            foreach (SecurityIdentifier sid in GroupSids)
            {
                try
                {
                    var account = (NTAccount)sid.Translate(typeof(NTAccount));
                    friendlySIDs.Add(new PermissionGroup
                    {
                        FriendlyName = account.Value,
                        SID = sid
                    });
                }
                catch (Exception ex)
                {
                    continue;
                }
            }
            return friendlySIDs;
        }
    }
}
