using System.ComponentModel;
using Runnatics.Models.Data.Common;

namespace Runnatics.Models.Data.Common
{
    public static class UserRoleExtensions
    {
        public static string GetDescription(this UserRole role)
        {
            var fieldInfo = role.GetType().GetField(role.ToString());
            var attributes = (DescriptionAttribute[])fieldInfo?.GetCustomAttributes(typeof(DescriptionAttribute), false);
            return attributes?.Length > 0 ? attributes[0].Description : role.ToString();
        }

        public static string GetDisplayName(this UserRole role)
        {
            return role switch
            {
                UserRole.Admin => "Administrator",
                UserRole.Ops => "Operations",
                UserRole.Support => "Support",
                UserRole.ReadOnly => "Read Only",
                _ => role.ToString()
            };
        }

        public static bool CanManageUsers(this UserRole role)
        {
            return role == UserRole.Admin;
        }

        public static bool CanManageEvents(this UserRole role)
        {
            return role == UserRole.Admin || role == UserRole.Ops;
        }

        public static bool CanManageRaceDay(this UserRole role)
        {
            return role == UserRole.Admin || role == UserRole.Ops;
        }

        public static bool CanViewReports(this UserRole role)
        {
            return role == UserRole.Admin || role == UserRole.Ops;
        }

        public static bool CanSupportParticipants(this UserRole role)
        {
            return role == UserRole.Admin || role == UserRole.Ops || role == UserRole.Support;
        }

        public static bool CanViewResults(this UserRole role)
        {
            return true; // All roles can view results
        }

        public static bool CanInviteUsers(this UserRole role)
        {
            return role == UserRole.Admin || role == UserRole.Ops;
        }

        public static bool CanRevokeUsers(this UserRole role)
        {
            return role == UserRole.Admin;
        }

        public static List<string> GetPermissions(this UserRole role)
        {
            var permissions = new List<string>();

            if (role.CanViewResults()) permissions.Add("View Results");
            if (role.CanSupportParticipants()) permissions.Add("Support Participants");
            if (role.CanViewReports()) permissions.Add("View Reports");
            if (role.CanManageRaceDay()) permissions.Add("Manage Race Day");
            if (role.CanManageEvents()) permissions.Add("Manage Events");
            if (role.CanInviteUsers()) permissions.Add("Invite Users");
            if (role.CanManageUsers()) permissions.Add("Manage Users");
            if (role.CanRevokeUsers()) permissions.Add("Revoke Users");

            return permissions;
        }
    }
}