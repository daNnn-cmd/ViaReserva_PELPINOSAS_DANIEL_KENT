using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using ViaReservaERP.Models;

namespace ViaReservaERP.Security;

public class AuthSignInService : IAuthSignInService
{
    public async Task SignInAsync(HttpContext httpContext, ErpUser user, CancellationToken ct = default)
    {
        var roleName = RoleIdMapper.ToRoleName(user.RoleId);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new(ClaimTypes.Name, user.FullName),
            new(ClaimTypes.Email, user.Email),
            new(ClaimTypes.Role, roleName),
            new(ViaReservaClaims.RoleId, user.RoleId.ToString()),
            new(ViaReservaClaims.CompanyId, user.CompanyId.ToString()),
            new(ViaReservaClaims.UserId, user.UserId.ToString())
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddHours(12)
            });
    }
}
