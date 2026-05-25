using Microsoft.EntityFrameworkCore;
using ViaReservaERP.Data;
using ViaReservaERP.Models;

namespace ViaReservaERP.Services;

public class NotificationService : INotificationService
{
    private readonly ViaReservaDbContext _db;
    private readonly IEmailService _email;

    public NotificationService(ViaReservaDbContext db, IEmailService email)
    {
        _db = db;
        _email = email;
    }

    public async Task NotifyUserAsync(int userId, string title, string message, string type, int? companyId = null, CancellationToken ct = default)
    {
        _db.Notifications.Add(new Notification
        {
            UserId = userId,
            CompanyId = companyId,
            Title = title,
            Message = message,
            Type = type,
            IsRead = false,
            CreatedAt = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
    }

    public async Task NotifyRoleAsync(int companyId, int roleId, string title, string message, string type, CancellationToken ct = default)
    {
        var userIds = await _db.Users
            .AsNoTracking()
            .Where(u => u.CompanyId == companyId && u.RoleId == roleId && u.IsActive && !u.IsDeleted)
            .Select(u => u.UserId)
            .ToListAsync(ct);

        if (userIds.Count == 0)
            return;

        foreach (var userId in userIds)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = userId,
                CompanyId = companyId,
                Title = title,
                Message = message,
                Type = type,
                IsRead = false,
                CreatedAt = ViaReservaERP.AppTime.Now
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public Task NotifySuperAdminsAsync(string title, string message, string type, CancellationToken ct = default)
    {
        return NotifyAllUsersByRoleAsync(roleId: 1, title, message, type, ct);
    }

    private async Task NotifyAllUsersByRoleAsync(int roleId, string title, string message, string type, CancellationToken ct)
    {
        var userIds = await _db.Users
            .AsNoTracking()
            .Where(u => u.RoleId == roleId && u.IsActive && !u.IsDeleted)
            .Select(u => new { u.UserId, u.CompanyId })
            .ToListAsync(ct);

        if (userIds.Count == 0)
            return;

        foreach (var u in userIds)
        {
            _db.Notifications.Add(new Notification
            {
                UserId = u.UserId,
                CompanyId = u.CompanyId,
                Title = title,
                Message = message,
                Type = type,
                IsRead = false,
                CreatedAt = ViaReservaERP.AppTime.Now
            });
        }

        await _db.SaveChangesAsync(ct);
    }

    public Task EmailUserAsync(string toEmail, string subject, string plainText, string? html = null, CancellationToken ct = default)
    {
        return _email.SendAsync(toEmail, subject, plainText, html, ct);
    }
}
