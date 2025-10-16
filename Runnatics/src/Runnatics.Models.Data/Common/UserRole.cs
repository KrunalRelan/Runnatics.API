using System.ComponentModel;

namespace Runnatics.Models.Data.Common
{
    public enum UserRole
    {
        [Description("Administrator - Full system access")]
        Admin = 1,

        [Description("Operations - Race day and event management")]
        Ops = 2,

        [Description("Support - Participant support and queries")]
        Support = 3,

        [Description("Read Only - View access only")]
        ReadOnly = 4
    }

    
}