namespace ViaReservaERP.Security;

public static class RoleIdMapper
{
    public static string ToRoleName(int roleId) => roleId switch
    {
        1 => RoleNames.SuperAdmin,
        2 => RoleNames.CompanyAdmin,
        3 => RoleNames.Accountant,
        4 => RoleNames.FrontDesk,
        5 => RoleNames.ServiceStaff,
        6 => RoleNames.Guest,
        _ => "Unknown"
    };
}
