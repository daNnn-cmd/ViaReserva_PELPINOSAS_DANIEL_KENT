using ViaReservaERP.Models;

namespace ViaReservaERP.Security;

public interface IAuthSignInService
{
    Task SignInAsync(HttpContext httpContext, ErpUser user, CancellationToken ct = default);
}
