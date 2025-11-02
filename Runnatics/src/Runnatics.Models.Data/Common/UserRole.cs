using System.ComponentModel;

namespace Runnatics.Models.Data.Common
{
    public enum UserRole
    {
        [Description("Super Administrator - Full system access")]
        SuperAdmin = 1,

        [Description("Administrator - Full system access")]
        Admin = 2,

        [Description("Operations - Race day and event management")]
        Ops = 3,

        [Description("Support - Participant support and queries")]
        Support = 4,

        [Description("Read Only - View access only")]
        ReadOnly = 0
    }

    
}