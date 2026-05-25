using ViaReservaERP.Models;

namespace ViaReservaERP.Services;

public interface INotificationService
{
    Task NotifyUserAsync(int userId, string title, string message, string type, int? companyId = null, CancellationToken ct = default);

    Task NotifyRoleAsync(int companyId, int roleId, string title, string message, string type, CancellationToken ct = default);

    Task NotifySuperAdminsAsync(string title, string message, string type, CancellationToken ct = default);

    Task EmailUserAsync(string toEmail, string subject, string plainText, string? html = null, CancellationToken ct = default);
}
