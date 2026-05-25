using System.Security.Claims;

namespace ViaReservaERP.Security;

public static class ClaimsPrincipalExtensions
{
    public static int? GetRoleId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ViaReservaClaims.RoleId);
        return int.TryParse(value, out var roleId) ? roleId : null;
    }

    public static int? GetCompanyId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ViaReservaClaims.CompanyId);
        return int.TryParse(value, out var companyId) ? companyId : null;
    }

    public static int? GetUserId(this ClaimsPrincipal principal)
    {
        var value = principal.FindFirstValue(ViaReservaClaims.UserId);
        return int.TryParse(value, out var userId) ? userId : null;
    }

    public static bool HasRoleId(this ClaimsPrincipal principal, params int[] roleIds)
    {
        var roleId = principal.GetRoleId();
        return roleId.HasValue && roleIds.Contains(roleId.Value);
    }
}
