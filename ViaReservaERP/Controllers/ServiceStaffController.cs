using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ViaReservaERP.Data;
using ViaReservaERP.Models;
using ViaReservaERP.Models.Admin;
using ViaReservaERP.Models.ServiceStaff;
using ViaReservaERP.Services;
using ViaReservaERP.Security;

namespace ViaReservaERP.Controllers;

[Authorize(Policy = RoleNames.ServiceStaff)]
public class ServiceStaffController : Controller
{
    private readonly ViaReservaDbContext _db;
    private readonly IServiceRequestAppService _serviceRequests;
    private readonly IWebHostEnvironment _env;

    public ServiceStaffController(ViaReservaDbContext db, IServiceRequestAppService serviceRequests, IWebHostEnvironment env)
    {
        _db = db;
        _serviceRequests = serviceRequests;
        _env = env;
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard(CancellationToken ct = default)
    {
        ViewData["Title"] = "Service Staff Dashboard";
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (!userId.HasValue || !companyId.HasValue)
            return Forbid();

        var assigned = _db.ServiceRequests
            .AsNoTracking()
            .Where(sr => sr.CompanyId == companyId.Value)
            .Where(sr => sr.AssignedTo == userId.Value);

        var pending = await assigned.CountAsync(sr => (sr.Status ?? "") == "Pending" || (sr.Status ?? "") == "Assigned", ct);
        var inProgress = await assigned.CountAsync(sr => (sr.Status ?? "") == "In Progress", ct);
        var completed = await assigned.CountAsync(sr => (sr.Status ?? "") == "Completed", ct);

        var recentRaw = await assigned
            .Include(sr => sr.Guest)
            .Include(sr => sr.Service)
            .OrderByDescending(sr => sr.RequestDate)
            .Take(10)
            .Select(sr => new AssignedServiceRequestRow
            {
                RequestId = sr.RequestId,
                GuestId = sr.GuestId,
                GuestName = sr.Guest != null ? (sr.Guest.FullName ?? "") : "",
                ServiceName = sr.Service != null ? (sr.Service.ServiceName ?? "") : "",
                Status = sr.Status ?? "",
                RequestDateUtc = sr.RequestDate
            })
            .ToListAsync(ct);

        var recentGuestIds = recentRaw
            .Where(r => r.GuestId.HasValue)
            .Select(r => r.GuestId!.Value)
            .Distinct()
            .ToList();

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        var guestRoomMap = await _db.ReservationRooms
            .AsNoTracking()
            .Include(rr => rr.Room)
            .Include(rr => rr.Reservation)
            .Where(rr => rr.Reservation != null && rr.Room != null)
            .Where(rr => rr.Reservation!.CompanyId == companyId.Value)
            .Where(rr => recentGuestIds.Contains(rr.Reservation!.GuestId))
            .Where(rr => rr.Reservation!.Status != "Cancelled" && rr.Reservation.Status != "Completed")
            .Where(rr => rr.Reservation!.CheckInDate.HasValue && rr.Reservation.CheckOutDate.HasValue)
            .Where(rr => rr.Reservation!.CheckInDate!.Value <= today && rr.Reservation.CheckOutDate!.Value >= today)
            .OrderByDescending(rr => rr.Reservation!.Status == "Checked In" ? 1 : 0)
            .ThenByDescending(rr => rr.ReservationId)
            .Select(rr => new
            {
                GuestId = rr.Reservation!.GuestId,
                RoomNumber = rr.Room!.RoomNumber
            })
            .ToListAsync(ct);

        var roomByGuest = guestRoomMap
            .GroupBy(x => x.GuestId)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.RoomNumber ?? "", comparer: EqualityComparer<int>.Default);

        foreach (var row in recentRaw)
        {
            if (row.GuestId.HasValue && roomByGuest.TryGetValue(row.GuestId.Value, out var rn))
                row.RoomNumber = rn ?? "";
        }

        var recent = recentRaw;

        var model = new ServiceStaffDashboardViewModel
        {
            Pending = pending,
            InProgress = inProgress,
            Completed = completed,
            Recent = recent
        };

        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> DashboardStats(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (!userId.HasValue || !companyId.HasValue)
            return Forbid();

        var assigned = _db.ServiceRequests
            .AsNoTracking()
            .Where(sr => sr.CompanyId == companyId.Value)
            .Where(sr => sr.AssignedTo == userId.Value);

        var pending = await assigned.CountAsync(sr => (sr.Status ?? "") == "Pending" || (sr.Status ?? "") == "Assigned", ct);
        var inProgress = await assigned.CountAsync(sr => (sr.Status ?? "") == "In Progress", ct);
        var completed = await assigned.CountAsync(sr => (sr.Status ?? "") == "Completed", ct);

        return Json(new { pending, inProgress, completed });
    }

    [HttpGet]
    public async Task<IActionResult> DashboardRecentPartial(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (!userId.HasValue || !companyId.HasValue)
            return Forbid();

        var assigned = _db.ServiceRequests
            .AsNoTracking()
            .Where(sr => sr.CompanyId == companyId.Value)
            .Where(sr => sr.AssignedTo == userId.Value);

        var recentRaw = await assigned
            .Include(sr => sr.Guest)
            .Include(sr => sr.Service)
            .OrderByDescending(sr => sr.RequestDate)
            .Take(10)
            .Select(sr => new AssignedServiceRequestRow
            {
                RequestId = sr.RequestId,
                GuestId = sr.GuestId,
                GuestName = sr.Guest != null ? (sr.Guest.FullName ?? "") : "",
                ServiceName = sr.Service != null ? (sr.Service.ServiceName ?? "") : "",
                Status = sr.Status ?? "",
                RequestDateUtc = sr.RequestDate
            })
            .ToListAsync(ct);

        var recentGuestIds = recentRaw
            .Where(r => r.GuestId.HasValue)
            .Select(r => r.GuestId!.Value)
            .Distinct()
            .ToList();

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        var guestRoomMap = await _db.ReservationRooms
            .AsNoTracking()
            .Include(rr => rr.Room)
            .Include(rr => rr.Reservation)
            .Where(rr => rr.Reservation != null && rr.Room != null)
            .Where(rr => rr.Reservation!.CompanyId == companyId.Value)
            .Where(rr => recentGuestIds.Contains(rr.Reservation!.GuestId))
            .Where(rr => rr.Reservation!.Status != "Cancelled" && rr.Reservation.Status != "Completed")
            .Where(rr => rr.Reservation!.CheckInDate.HasValue && rr.Reservation.CheckOutDate.HasValue)
            .Where(rr => rr.Reservation!.CheckInDate!.Value <= today && rr.Reservation.CheckOutDate!.Value >= today)
            .OrderByDescending(rr => rr.Reservation!.Status == "Checked In" ? 1 : 0)
            .ThenByDescending(rr => rr.ReservationId)
            .Select(rr => new
            {
                GuestId = rr.Reservation!.GuestId,
                RoomNumber = rr.Room!.RoomNumber
            })
            .ToListAsync(ct);

        var roomByGuest = guestRoomMap
            .GroupBy(x => x.GuestId)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.RoomNumber ?? "", comparer: EqualityComparer<int>.Default);

        foreach (var row in recentRaw)
        {
            if (row.GuestId.HasValue && roomByGuest.TryGetValue(row.GuestId.Value, out var rn))
                row.RoomNumber = rn ?? "";
        }

        return PartialView("_DashboardRecent", recentRaw);
    }

    [HttpGet]
    public async Task<IActionResult> Requests(CancellationToken ct = default)
    {
        ViewData["Title"] = "My Service Requests";
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (!userId.HasValue || !companyId.HasValue)
            return Forbid();

        var rowsRaw = await _db.ServiceRequests
            .AsNoTracking()
            .Include(sr => sr.Guest)
            .Include(sr => sr.Service)
            .Where(sr => sr.CompanyId == companyId.Value)
            .Where(sr => sr.AssignedTo == userId.Value)
            .OrderByDescending(sr => sr.RequestDate)
            .Take(250)
            .Select(sr => new AssignedServiceRequestRow
            {
                RequestId = sr.RequestId,
                GuestId = sr.GuestId,
                GuestName = sr.Guest != null ? (sr.Guest.FullName ?? "") : "",
                ServiceName = sr.Service != null ? (sr.Service.ServiceName ?? "") : "",
                Status = sr.Status ?? "",
                RequestDateUtc = sr.RequestDate
            })
            .ToListAsync(ct);

        var guestIds = rowsRaw
            .Where(r => r.GuestId.HasValue)
            .Select(r => r.GuestId!.Value)
            .Distinct()
            .ToList();

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        var guestRoomMap = await _db.ReservationRooms
            .AsNoTracking()
            .Include(rr => rr.Room)
            .Include(rr => rr.Reservation)
            .Where(rr => rr.Reservation != null && rr.Room != null)
            .Where(rr => rr.Reservation!.CompanyId == companyId.Value)
            .Where(rr => guestIds.Contains(rr.Reservation!.GuestId))
            .Where(rr => rr.Reservation!.Status != "Cancelled" && rr.Reservation.Status != "Completed")
            .Where(rr => rr.Reservation!.CheckInDate.HasValue && rr.Reservation.CheckOutDate.HasValue)
            .Where(rr => rr.Reservation!.CheckInDate!.Value <= today && rr.Reservation.CheckOutDate!.Value >= today)
            .OrderByDescending(rr => rr.Reservation!.Status == "Checked In" ? 1 : 0)
            .ThenByDescending(rr => rr.ReservationId)
            .Select(rr => new
            {
                GuestId = rr.Reservation!.GuestId,
                RoomNumber = rr.Room!.RoomNumber
            })
            .ToListAsync(ct);

        var roomByGuest = guestRoomMap
            .GroupBy(x => x.GuestId)
            .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.RoomNumber ?? "", comparer: EqualityComparer<int>.Default);

        foreach (var row in rowsRaw)
        {
            if (row.GuestId.HasValue && roomByGuest.TryGetValue(row.GuestId.Value, out var rn))
                row.RoomNumber = rn ?? "";
        }

        var rows = rowsRaw;

        return View(rows);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStatus(int id, string status, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (!userId.HasValue || !companyId.HasValue)
            return Forbid();

        if (string.IsNullOrWhiteSpace(status))
        {
            TempData["ServiceStaffErrorMessage"] = "Please select a valid status.";
            return RedirectToAction(nameof(Requests));
        }

        var request = await _db.ServiceRequests
            .Include(sr => sr.Service)
            .Include(sr => sr.Guest)
            .FirstOrDefaultAsync(sr => sr.RequestId == id && sr.CompanyId == companyId.Value && sr.AssignedTo == userId.Value, ct);

        if (request == null)
        {
            TempData["ServiceStaffErrorMessage"] = "Service request not found or not assigned to you.";
            return RedirectToAction(nameof(Requests));
        }

        var oldStatus = request.Status ?? "Unknown";
        await _serviceRequests.UpdateStatusAsync(id, status, performedByUserId: userId.Value, ct);

        var serviceName = request.Service?.ServiceName ?? "Service";
        var guestName = request.Guest?.FullName ?? "Guest";
        
        TempData["ServiceStaffSuccessMessage"] = $"{serviceName} for {guestName} updated: {oldStatus} → {status}";
        TempData["ToastStatus"] = status;

        return RedirectToAction(nameof(Requests));
    }

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken ct = default)
    {
        ViewData["Title"] = "Profile Settings";
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (!userId.HasValue || !companyId.HasValue)
            return Forbid();

        var user = await _db.Users
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.UserId == userId.Value && u.CompanyId == companyId.Value, ct);

        if (user == null) return NotFound();

        ViewData["AvatarUrl"] = user.AvatarUrl;

        return View(user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAvatar(IFormFile avatar, CancellationToken ct = default)
    {
        var userId = User.GetUserId() ?? 0;
        if (userId <= 0) return Forbid();

        if (avatar == null || avatar.Length == 0)
        {
            TempData["ErrorMessage"] = "Please select a valid image file.";
            return RedirectToAction(nameof(Profile));
        }

        var ext = Path.GetExtension(avatar.FileName).ToLower();
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        if (!allowed.Contains(ext))
        {
            TempData["ErrorMessage"] = "Only JPG, PNG and WEBP images are allowed.";
            return RedirectToAction(nameof(Profile));
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct);
        if (user == null) return NotFound();

        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "avatars");
        if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

        var fileName = $"avatar_{userId}_{DateTime.Now.Ticks}{ext}";
        var filePath = Path.Combine(uploadsDir, fileName);

        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await avatar.CopyToAsync(stream, ct);
        }

        if (!string.IsNullOrEmpty(user.AvatarUrl))
        {
            var oldPath = Path.Combine(_env.WebRootPath, user.AvatarUrl.TrimStart('/'));
            if (System.IO.File.Exists(oldPath))
            {
                try { System.IO.File.Delete(oldPath); } catch { /* ignore */ }
            }
        }

        user.AvatarUrl = $"/uploads/avatars/{fileName}";
        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = "Avatar updated successfully.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(string fullName, string? newPassword, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (!userId.HasValue || !companyId.HasValue)
            return Forbid();

        if (string.IsNullOrWhiteSpace(fullName))
        {
            TempData["ErrorMessage"] = "Full name is required.";
            return RedirectToAction(nameof(Profile));
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId.Value && u.CompanyId == companyId.Value, ct);
        if (user == null) return NotFound();

        user.FullName = fullName.Trim();
        user.UpdatedAt = ViaReservaERP.AppTime.Now;

        if (!string.IsNullOrWhiteSpace(newPassword))
        {
            if (newPassword.Length < 6)
            {
                TempData["ErrorMessage"] = "Password must be at least 6 characters.";
                return RedirectToAction(nameof(Profile));
            }

            user.PasswordHash = PasswordHasher.Hash(newPassword);
        }

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId.Value,
            UserId = userId.Value,
            Action = "Update",
            TableName = "Users",
            RecordId = userId.Value,
            NewValues = "Profile updated",
            IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers["User-Agent"].ToString(),
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Profile updated.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    public async Task<IActionResult> Notifications(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (!userId.HasValue || !companyId.HasValue)
            return Forbid();

        var items = await _db.Notifications
            .Where(n => n.CompanyId == companyId.Value && n.UserId == userId.Value)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var model = new AdminNotificationsViewModel
        {
            Items = items,
            UnreadCount = items.Count(n => !n.IsRead)
        };

        ViewData["Title"] = "Notifications";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> NotificationsUnreadCount(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (!userId.HasValue || !companyId.HasValue)
            return Forbid();

        var unread = await _db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.CompanyId == companyId.Value && n.UserId == userId.Value && !n.IsRead, ct);

        return Json(new { unread });
    }

    [HttpGet]
    public async Task<IActionResult> NotificationsListPartial(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (!userId.HasValue || !companyId.HasValue)
            return Forbid();

        var items = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.CompanyId == companyId.Value && n.UserId == userId.Value)
            .OrderByDescending(n => n.CreatedAt)
            .Take(50)
            .ToListAsync(ct);

        var model = new AdminNotificationsViewModel
        {
            Items = items,
            UnreadCount = items.Count(n => !n.IsRead)
        };

        return PartialView("_NotificationsList", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (!userId.HasValue || !companyId.HasValue)
            return Forbid();

        var unread = await _db.Notifications
            .Where(n => n.CompanyId == companyId.Value && n.UserId == userId.Value && !n.IsRead)
            .ToListAsync(ct);

        foreach (var n in unread)
        {
            n.IsRead = true;
        }

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "All notifications marked as read.";
        return RedirectToAction(nameof(Notifications));
    }
}
