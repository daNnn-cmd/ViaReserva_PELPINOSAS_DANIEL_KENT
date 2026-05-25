using ViaReservaERP.Models;

namespace ViaReservaERP.Models.SuperAdmin;

public class RolesPermissionsViewModel
{
    public int TotalRoles { get; set; }
    public int TotalPermissions { get; set; }
    public int CustomRolesCreated { get; set; }

    public int? SelectedRoleId { get; set; }
    public string? PermissionSearch { get; set; }

    public List<Role> Roles { get; set; } = new();
    public List<Permission> Permissions { get; set; } = new();
    public HashSet<int> SelectedRolePermissionIds { get; set; } = new();
}
