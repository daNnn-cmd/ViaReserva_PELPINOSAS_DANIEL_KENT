using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Globalization;
using System.Linq;
using System.Text;
using ClosedXML.Excel;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using ViaReservaERP.Data;
using ViaReservaERP.Models;
using ViaReservaERP.Models.Admin;
using ViaReservaERP.Security;
using ViaReservaERP.Services;

namespace ViaReservaERP.Controllers;

[Authorize(Policy = RoleNames.CompanyAdmin)]
public class AdminController : Controller
{
    private readonly ViaReservaDbContext _db;
    private readonly IServiceRequestAppService _serviceRequests;
    private readonly INotificationService _notify;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly IStripePaymentService _stripe;
    private readonly IBookingCheckoutService _checkout;

    private static readonly TimeZoneInfo PhTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Singapore Standard Time");

    public AdminController(ViaReservaDbContext db, IServiceRequestAppService serviceRequests, INotificationService notify, IWebHostEnvironment env, IConfiguration config, IStripePaymentService stripe, IBookingCheckoutService checkout)
    {
        _db = db;
        _serviceRequests = serviceRequests;
        _notify = notify;
        _env = env;
        _config = config;
        _stripe = stripe;
        _checkout = checkout;
    }

    private int CurrentCompanyId => User.GetCompanyId() ?? 0;
    private int CurrentUserId => User.GetUserId() ?? 0;

    public async Task<IActionResult> Dashboard(string granularity = "month", DateOnly? startDate = null, DateOnly? endDate = null, int? year = null, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var now = ViaReservaERP.AppTime.Now;
        var today = DateOnly.FromDateTime(now);

        if (year.HasValue)
        {
            startDate = new DateOnly(year.Value, 1, 1);
            endDate = new DateOnly(year.Value, 12, 31);
        }

        granularity = string.IsNullOrWhiteSpace(granularity) ? "month" : granularity.Trim().ToLowerInvariant();
        if (granularity != "day" && granularity != "month" && granularity != "year") granularity = "month";

        DateOnly start;
        DateOnly end;
        if (startDate.HasValue && endDate.HasValue)
        {
            start = startDate.Value;
            end = endDate.Value;
            if (end < start) (start, end) = (end, start);
        }
        else
        {
            start = new DateOnly(today.Year, today.Month, 1);
            end = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        }

        var startUtc = DateTime.SpecifyKind(start.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var endUtcExcl = DateTime.SpecifyKind(end.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        // Comparison Range
        var days = end.DayNumber - start.DayNumber;
        var compareEnd = start.AddDays(-1);
        var compareStart = compareEnd.AddDays(-days);
        var cStartUtc = DateTime.SpecifyKind(compareStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var cEndUtcExcl = DateTime.SpecifyKind(compareEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

        // Basic Counts (Filtered by Company)
        var resCount = await _db.Reservations.CountAsync(r => r.CompanyId == companyId && r.CreatedAt >= startUtc && r.CreatedAt < endUtcExcl, ct);
        var cResCount = await _db.Reservations.CountAsync(r => r.CompanyId == companyId && r.CreatedAt >= cStartUtc && r.CreatedAt < cEndUtcExcl, ct);

        var guestsCount = await _db.Guests.CountAsync(g => g.CompanyId == companyId && g.CreatedAt >= startUtc && g.CreatedAt < endUtcExcl, ct);
        var cGuestsCount = await _db.Guests.CountAsync(g => g.CompanyId == companyId && g.CreatedAt >= cStartUtc && g.CreatedAt < cEndUtcExcl, ct);

        var staffCount = await _db.Users.CountAsync(u => u.CompanyId == companyId && u.RoleId != 6, ct);
        
        var pendingReq = await _db.ServiceRequests.CountAsync(sr => sr.CompanyId == companyId && sr.Status == "Pending", ct);

        // Advanced KPIs
        var occupied = await _db.ReservationRooms
            .Include(rr => rr.Reservation)
            .Where(rr => rr.Reservation != null && rr.Reservation.CompanyId == companyId && rr.Reservation.Status == "Checked In")
            .Select(rr => rr.RoomId).Distinct().CountAsync(ct);
        var totalRooms = await _db.Rooms.CountAsync(r => r.CompanyId == companyId && !r.IsDeleted, ct);
        var occRate = totalRooms > 0 ? (decimal)occupied / totalRooms * 100 : 0;

        var revMonth = await _db.Payments
            .Where(p => p.CompanyId == companyId && p.CreatedAt >= startUtc && p.CreatedAt < endUtcExcl && p.Status == "Succeeded")
            .SumAsync(p => p.Amount ?? 0m, ct);
        var cRevMonth = await _db.Payments
            .Where(p => p.CompanyId == companyId && p.CreatedAt >= cStartUtc && p.CreatedAt < cEndUtcExcl && p.Status == "Succeeded")
            .SumAsync(p => p.Amount ?? 0m, ct);

        var pendingApprovals = await _db.WorkflowInstances.CountAsync(wi => wi.CompanyId == companyId && wi.Status == "Pending", ct);

        static double? PctDelta(decimal cur, decimal prev) => prev == 0 ? (cur > 0 ? 100 : (double?)null) : (double)((cur - prev) / prev * 100);

        var model = new AdminDashboardViewModel
        {
            Granularity = granularity,
            SelectedYear = year,
            StartDate = start,
            EndDate = end,
            CompareStartDate = compareStart,
            CompareEndDate = compareEnd,
            SelectedRangeLabel = $"{start:MMM dd} - {end:MMM dd, yyyy}",
            ReservationsCount = resCount,
            ReservationsDeltaPct = PctDelta(resCount, cResCount),
            GuestsCount = guestsCount,
            GuestsDeltaPct = PctDelta(guestsCount, cGuestsCount),
            StaffCount = staffCount,
            PendingRequestsCount = pendingReq,
            ActiveStays = occupied,
            OccupancyRate = Math.Round(occRate, 1),
            RevenueMonth = revMonth,
            RevenueMonthDeltaPct = PctDelta(revMonth, cRevMonth),
            PendingApprovals = pendingApprovals,
            TotalRevenue = revMonth // Scoped to range
        };

        // --- TREND ANALYTICS ---
        var labels = new List<string>();
        var revData = new List<decimal>();
        var cRevData = new List<decimal>();
        var resData = new List<decimal>();
        var checkInData = new List<decimal>();
        var checkOutData = new List<decimal>();
        var cancelData = new List<decimal>();

        if (granularity == "day")
        {
            for (var d = start; d <= end; d = d.AddDays(1))
            {
                labels.Add(d.ToString("MMM dd"));
                var dUtc = DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                var dUtcExcl = dUtc.AddDays(1);
                
                var r = await _db.Payments.Where(p => p.CompanyId == companyId && p.Status == "Succeeded" && p.CreatedAt >= dUtc && p.CreatedAt < dUtcExcl).SumAsync(p => p.Amount ?? 0m, ct);
                revData.Add(r);
                
                var bc = await _db.Reservations.CountAsync(res => res.CompanyId == companyId && res.CreatedAt >= dUtc && res.CreatedAt < dUtcExcl, ct);
                resData.Add(bc);

                checkInData.Add(await _db.Reservations.CountAsync(res => res.CompanyId == companyId && res.Status == "Checked In" && res.CheckInDate == d, ct));
                checkOutData.Add(await _db.Reservations.CountAsync(res => res.CompanyId == companyId && res.Status == "Completed" && res.CheckOutDate == d, ct));
                cancelData.Add(await _db.Reservations.CountAsync(res => res.CompanyId == companyId && res.Status == "Cancelled" && res.CreatedAt >= dUtc && res.CreatedAt < dUtcExcl, ct));
            }
        }
        else if (granularity == "year")
        {
            for (var i = 4; i >= 0; i--)
            {
                var targetYear = today.Year - i;
                labels.Add(targetYear.ToString());
                var yStart = new DateOnly(targetYear, 1, 1);
                var yEnd = new DateOnly(targetYear, 12, 31);
                var ysUtc = DateTime.SpecifyKind(yStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                var yeUtcExcl = DateTime.SpecifyKind(yEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

                revData.Add(await _db.Payments.Where(p => p.CompanyId == companyId && p.Status == "Succeeded" && p.CreatedAt >= ysUtc && p.CreatedAt < yeUtcExcl).SumAsync(p => p.Amount ?? 0m, ct));
                resData.Add(await _db.Reservations.CountAsync(res => res.CompanyId == companyId && res.CreatedAt >= ysUtc && res.CreatedAt < yeUtcExcl, ct));
                checkInData.Add(await _db.Reservations.CountAsync(res => res.CompanyId == companyId && res.Status == "Checked In" && res.CheckInDate >= yStart && res.CheckInDate <= yEnd, ct));
                checkOutData.Add(await _db.Reservations.CountAsync(res => res.CompanyId == companyId && res.Status == "Completed" && res.CheckOutDate >= yStart && res.CheckOutDate <= yEnd, ct));
                cancelData.Add(await _db.Reservations.CountAsync(res => res.CompanyId == companyId && res.Status == "Cancelled" && res.CreatedAt >= ysUtc && res.CreatedAt < yeUtcExcl, ct));
            }
        }
        else // month
        {
            for (var m = 0; m < 6; m++)
            {
                var target = today.AddMonths(-5 + m);
                var mStart = new DateOnly(target.Year, target.Month, 1);
                var mEnd = new DateOnly(target.Year, target.Month, DateTime.DaysInMonth(target.Year, target.Month));
                labels.Add(mStart.ToString("MMM yyyy"));

                var msUtc = DateTime.SpecifyKind(mStart.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
                var meUtcExcl = DateTime.SpecifyKind(mEnd.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);

                revData.Add(await _db.Payments.Where(p => p.CompanyId == companyId && p.Status == "Succeeded" && p.CreatedAt >= msUtc && p.CreatedAt < meUtcExcl).SumAsync(p => p.Amount ?? 0m, ct));
                resData.Add(await _db.Reservations.CountAsync(res => res.CompanyId == companyId && res.CreatedAt >= msUtc && res.CreatedAt < meUtcExcl, ct));
                checkInData.Add(await _db.Reservations.CountAsync(res => res.CompanyId == companyId && res.Status == "Checked In" && res.CheckInDate >= mStart && res.CheckInDate <= mEnd, ct));
                checkOutData.Add(await _db.Reservations.CountAsync(res => res.CompanyId == companyId && res.Status == "Completed" && res.CheckOutDate >= mStart && res.CheckOutDate <= mEnd, ct));
                cancelData.Add(await _db.Reservations.CountAsync(res => res.CompanyId == companyId && res.Status == "Cancelled" && res.CreatedAt >= msUtc && res.CreatedAt < meUtcExcl, ct));
            }
        }

        model.RevenueAnalytics.Labels = labels;
        model.RevenueAnalytics.Datasets["Revenue"] = revData;

        model.ReservationAnalytics.Labels = labels;
        model.ReservationAnalytics.Datasets["Bookings"] = resData;
        model.ReservationAnalytics.Datasets["Check-ins"] = checkInData;
        model.ReservationAnalytics.Datasets["Check-outs"] = checkOutData;
        model.ReservationAnalytics.Datasets["Cancellations"] = cancelData;

        // Service Analytics
        var topServices = await _db.ServiceRequests
            .Include(sr => sr.Service)
            .Where(sr => sr.CompanyId == companyId && sr.RequestDate >= startUtc && sr.RequestDate < endUtcExcl)
            .GroupBy(sr => sr.Service!.ServiceName)
            .Select(g => new { Name = g.Key ?? "Unknown", Count = (decimal)g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(5)
            .ToListAsync(ct);

        model.ServiceAnalytics.Labels = topServices.Select(x => x.Name).ToList();
        model.ServiceAnalytics.Datasets["Most Requested"] = topServices.Select(x => x.Count).ToList();

        var pendingSvc = await _db.ServiceRequests.CountAsync(sr => sr.CompanyId == companyId && sr.Status == "Pending", ct);
        var completedSvc = await _db.ServiceRequests.CountAsync(sr => sr.CompanyId == companyId && sr.Status == "Completed", ct);
        model.ServiceAnalytics.Datasets["Pending vs Completed"] = new List<decimal> { pendingSvc, completedSvc };

        // Workflow
        model.WorkflowAnalytics.Datasets["Pending"] = new List<decimal> { pendingApprovals };
        model.WorkflowAnalytics.Datasets["Completed"] = new List<decimal> { await _db.WorkflowInstances.CountAsync(wi => wi.CompanyId == companyId && wi.Status == "Completed", ct) };
        model.WorkflowAnalytics.Datasets["Escalated"] = new List<decimal> { await _db.WorkflowInstances.CountAsync(wi => wi.CompanyId == companyId && wi.Status == "Escalated", ct) };

        // Financial
        var income = await _db.Transactions.Where(t => t.CompanyId == companyId && t.Type == "Income" && t.TransactionDate >= startUtc && t.TransactionDate < endUtcExcl).SumAsync(t => t.Amount ?? 0m, ct);
        var expense = await _db.Transactions.Where(t => t.CompanyId == companyId && t.Type == "Expense" && t.TransactionDate >= startUtc && t.TransactionDate < endUtcExcl).SumAsync(t => t.Amount ?? 0m, ct);
        model.FinancialAnalytics.Labels = new List<string> { "Current Period" };
        model.FinancialAnalytics.Datasets["Income"] = new List<decimal> { income };
        model.FinancialAnalytics.Datasets["Expenses"] = new List<decimal> { expense };
        model.FinancialAnalytics.Datasets["Profit"] = new List<decimal> { income - expense };

        // Recent
        model.RecentReservations = await _db.Reservations
            .Include(r => r.Guest)
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.ReservationId)
            .Take(5)
            .Select(r => new ReservationSummary
            {
                ReservationId = r.ReservationId,
                GuestName = r.Guest != null ? r.Guest.FullName : "Unknown",
                Status = r.Status ?? "Pending",
                Date = r.CreatedAt,
                Amount = r.TotalAmount ?? 0m
            })
            .ToListAsync(ct);

        ViewData["Title"] = "Admin Dashboard";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SendTestEmail(string recipientEmail, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(recipientEmail))
        {
            TempData["ErrorMessage"] = "Recipient email is required.";
            return RedirectToAction(nameof(Dashboard));
        }

        try
        {
            var subject = "VíaReserva ERP - SendGrid Test";
            var body = $@"
                <div style='font-family: sans-serif; padding: 20px; border: 1px solid #e2e8f0; border-radius: 8px; max-width: 600px;'>
                    <h2 style='color: #1a2a6c;'>Email System Verified</h2>
                    <p>This is a test email from your VíaReserva ERP system to verify that the SendGrid API integration is working correctly.</p>
                    <hr style='border: 0; border-top: 1px solid #e2e8f0; margin: 20px 0;' />
                    <p style='font-size: 0.875rem; color: #64748b;'>Timestamp: {ViaReservaERP.AppTime.Now:yyyy-MM-dd HH:mm:ss}</p>
                </div>";

            await _notify.EmailUserAsync(recipientEmail, subject, "SendGrid Test Successful!", body, ct);
            TempData["SuccessMessage"] = $"Test email sent successfully to {recipientEmail}. Please check your inbox (and spam folder).";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Failed to send test email: {ex.Message}";
        }

        return RedirectToAction(nameof(Dashboard));
    }

    public async Task<IActionResult> Reservations(string? searchGuest, string? statusFilter, DateOnly? startDate, DateOnly? endDate, 
        string sort = "id", string dir = "desc", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);
        
        if (!startDate.HasValue) startDate = new DateOnly(today.Year, today.Month, 1);
        if (!endDate.HasValue) endDate = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

        var query = _db.Reservations
            .Include(r => r.Guest)
            .Include(r => r.ReservationRooms)
                .ThenInclude(rr => rr.Room)
            .Where(r => r.CompanyId == companyId);

        if (!string.IsNullOrEmpty(searchGuest))
        {
            query = query.Where(r => r.Guest.FullName.Contains(searchGuest) || r.Guest.Email.Contains(searchGuest));
        }

        if (!string.IsNullOrEmpty(statusFilter))
        {
            query = query.Where(r => r.Status == statusFilter);
        }

        if (startDate.HasValue)
        {
            query = query.Where(r => r.CheckInDate >= startDate.Value);
        }

        if (endDate.HasValue)
        {
            query = query.Where(r => r.CheckOutDate <= endDate.Value);
        }

        // Sorting
        query = sort.ToLower() switch
        {
            "id" => dir == "asc" ? query.OrderBy(r => r.ReservationId) : query.OrderByDescending(r => r.ReservationId),
            "guest" => dir == "asc" ? query.OrderBy(r => r.Guest.FullName) : query.OrderByDescending(r => r.Guest.FullName),
            "checkin" => dir == "asc" ? query.OrderBy(r => r.CheckInDate) : query.OrderByDescending(r => r.CheckInDate),
            "status" => dir == "asc" ? query.OrderBy(r => r.Status) : query.OrderByDescending(r => r.Status),
            _ => query.OrderByDescending(r => r.ReservationId)
        };

        var totalRows = await query.CountAsync(ct);
        var skip = (page - 1) * pageSize;
        var reservations = await query.Skip(skip).Take(pageSize).ToListAsync(ct);

        // Daily Stats
        var baseStats = _db.Reservations.Where(r => r.CompanyId == companyId);
        
        var model = new Models.Admin.ReservationsViewModel
        {
            Rows = reservations,
            SearchGuest = searchGuest,
            StatusFilter = statusFilter,
            StartDate = startDate,
            EndDate = endDate,
            Sort = sort,
            Dir = dir,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            TotalToday = await baseStats.CountAsync(r => r.CheckInDate == today || r.CheckOutDate == today, ct),
            PendingCount = await baseStats.CountAsync(r => r.Status == "Pending", ct),
            CheckInsToday = await baseStats.CountAsync(r => r.CheckInDate == today, ct),
            CheckOutsToday = await baseStats.CountAsync(r => r.CheckOutDate == today, ct),
            AvailableServices = await _db.Services
                .Where(s => s.CompanyId == companyId)
                .OrderBy(s => s.ServiceName)
                .Select(s => new SelectListItem 
                { 
                    Value = s.ServiceId.ToString(), 
                    Text = s.ServiceName + " - ₱ " + (s.Price.HasValue ? s.Price.Value.ToString() : "0.00")
                })
                .ToListAsync(ct),
            ActiveReservations = await _db.Reservations
                .Where(r => r.CompanyId == companyId && (r.Status == "Confirmed" || r.Status == "Checked In"))
                .OrderByDescending(r => r.ReservationId)
                .Select(r => new SelectListItem
                {
                    Value = r.ReservationId.ToString(),
                    Text = $"#{r.ReservationId} - {r.Guest.FullName}"
                })
                .ToListAsync(ct),
            CancelledCount = await baseStats.CountAsync(r => r.Status == "Cancelled", ct),
            RevenueToday = await _db.Payments
                .Where(p => p.CompanyId == companyId && DateOnly.FromDateTime(p.CreatedAt) == today && p.Status == "Succeeded")
                .SumAsync(p => p.Amount.HasValue ? p.Amount.Value : 0m, ct)
        };
        ViewData["Title"] = "Reservation Management";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelReservation(int id, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var res = await _db.Reservations
            .Include(r => r.Payments)
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room)
            .Include(r => r.Guest)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.ReservationId == id, ct);
        if (res == null) return NotFound();

        var status = (res.Status ?? "").ToLowerInvariant();
        if (status.Contains("checked in") || status.Contains("checked out") || status.Contains("cancel"))
        {
            TempData["ErrorMessage"] = "Cannot cancel this reservation from its current status.";
            return RedirectToAction(nameof(Reservations));
        }

        res.Status = "Cancelled";
        foreach (var rr in res.ReservationRooms)
            if (rr.Room != null) rr.Room.Status = "Available";

        var payment = res.Payments.OrderByDescending(p => p.PaymentId).FirstOrDefault();
        if (payment != null && (payment.Status ?? "").ToLower().Contains("succeed"))
        {
            payment.Status = "Refund Pending";
            var total = res.TotalAmount ?? 0m;
            var refundAmount = payment.Amount ?? 0m;
            var ratio = (total > 0m && refundAmount > 0m) ? (refundAmount / total) : 0m;
            _db.Transactions.Add(new AccountingTransaction
            {
                CompanyId = companyId, Subtotal = (res.Subtotal ?? 0m) * -ratio,
                TaxAmount = (res.TaxAmount ?? 0m) * -ratio, ServiceCharge = (res.ServiceCharge ?? 0m) * -ratio,
                Amount = -(payment.Amount ?? 0m), Type = "Refund",
                Description = $"Admin Cancellation Refund #{res.ReservationId}",
                ReferenceId = res.ReservationId, ReferenceType = "Reservation",
                TransactionDate = ViaReservaERP.AppTime.Now
            });
        }

        _db.AuditLogs.Add(new AuditLog { CompanyId = companyId, UserId = CurrentUserId,
            Action = "Reservation Cancelled", TableName = "Reservations", RecordId = res.ReservationId,
            NewValues = $"Admin cancelled reservation #{res.ReservationId} for {res.Guest?.FullName}",
            ActionDate = ViaReservaERP.AppTime.Now });

        var guestUserId = res.Guest?.UserId;
        if (guestUserId.HasValue)
            _db.Notifications.Add(new Notification { UserId = guestUserId.Value, CompanyId = companyId,
                Title = "Reservation Cancelled", Message = $"Your reservation #{res.ReservationId} was cancelled by management. Refund: {(payment?.Status ?? "N/A")}.",
                Type = "Reservation", IsRead = false, CreatedAt = ViaReservaERP.AppTime.Now });

        await _db.SaveChangesAsync(ct);
        await _notify.NotifyRoleAsync(companyId, 3, "Cancellation Refund", $"Reservation #{res.ReservationId} cancelled. Refund: {(payment?.Status ?? "N/A")}.", "Reservation", ct);
        await _notify.NotifyRoleAsync(companyId, 4, "Reservation Cancelled", $"Reservation #{res.ReservationId} for {res.Guest?.FullName} cancelled by admin.", "Reservation", ct);

        TempData["SuccessMessage"] = $"Reservation #{res.ReservationId} cancelled.";
        return RedirectToAction(nameof(Reservations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExtendStay(int id, DateOnly newCheckOutDate, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var tax = new TaxService();
        var res = await _db.Reservations
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room).ThenInclude(room => room!.RoomType)
            .Include(r => r.Guest)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.ReservationId == id, ct);
        if (res == null) return NotFound();

        if (res.Status != "Checked In") { TempData["ErrorMessage"] = "Can only extend a checked-in reservation."; return RedirectToAction(nameof(Reservations)); }
        if (res.CheckOutDate == null || newCheckOutDate <= res.CheckOutDate.Value) { TempData["ErrorMessage"] = "New date must be after current check-out."; return RedirectToAction(nameof(Reservations)); }

        var oldCheckOut = res.CheckOutDate.Value;
        var additionalNights = newCheckOutDate.DayNumber - oldCheckOut.DayNumber;
        var roomPrice = res.ReservationRooms.FirstOrDefault()?.Room?.RoomType?.BasePrice ?? 0m;
        var taxResult = tax.CalculateTaxes(roomPrice * additionalNights, companyId);

        res.CheckOutDate = newCheckOutDate;
        res.Subtotal = (res.Subtotal ?? 0m) + taxResult.Subtotal;
        res.TaxAmount = (res.TaxAmount ?? 0m) + taxResult.TaxAmount;
        res.ServiceCharge = (res.ServiceCharge ?? 0m) + taxResult.ServiceCharge;
        res.TotalAmount = (res.TotalAmount ?? 0m) + taxResult.Total;

        _db.Transactions.Add(new AccountingTransaction { CompanyId = companyId, Subtotal = taxResult.Subtotal, TaxAmount = taxResult.TaxAmount,
            ServiceCharge = taxResult.ServiceCharge, Amount = taxResult.Total, Type = "Income",
            Description = $"Stay Extension (+{additionalNights} nights) Res #{res.ReservationId}",
            ReferenceId = res.ReservationId, ReferenceType = "Reservation", TransactionDate = ViaReservaERP.AppTime.Now });

        _db.AuditLogs.Add(new AuditLog { CompanyId = companyId, UserId = CurrentUserId, Action = "Stay Extended", TableName = "Reservations",
            RecordId = res.ReservationId, NewValues = $"Admin extended {oldCheckOut:yyyy-MM-dd} → {newCheckOutDate:yyyy-MM-dd} (+{additionalNights}n, +₱{taxResult.Total:N2})",
            ActionDate = ViaReservaERP.AppTime.Now });

        var guestUserId2 = res.Guest?.UserId;
        if (guestUserId2.HasValue)
            _db.Notifications.Add(new Notification { UserId = guestUserId2.Value, CompanyId = companyId,
                Title = "Stay Extended", Message = $"Your reservation #{res.ReservationId} has been extended to {newCheckOutDate:MMM dd, yyyy}. Additional: ₱{taxResult.Total:N2}.",
                Type = "Reservation", IsRead = false, CreatedAt = ViaReservaERP.AppTime.Now });

        await _db.SaveChangesAsync(ct);
        await _notify.NotifyRoleAsync(companyId, 3, "Stay Extension", $"Reservation #{res.ReservationId} extended +{additionalNights} nights (+₱{taxResult.Total:N2}).", "Reservation", ct);
        await _notify.NotifyRoleAsync(companyId, 4, "Stay Extended", $"Reservation #{res.ReservationId} extended to {newCheckOutDate:MMM dd, yyyy}.", "Reservation", ct);

        TempData["SuccessMessage"] = $"Reservation #{res.ReservationId} extended to {newCheckOutDate:MMM dd, yyyy}. +₱{taxResult.Total:N2}.";
        return RedirectToAction(nameof(Reservations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EarlyCheckOut(int id, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var tax = new TaxService();
        var res = await _db.Reservations
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room).ThenInclude(room => room!.RoomType)
            .Include(r => r.Payments).Include(r => r.Guest)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.ReservationId == id, ct);
        if (res == null) return NotFound();

        if (res.Status != "Checked In") { TempData["ErrorMessage"] = "Can only early check-out a checked-in reservation."; return RedirectToAction(nameof(Reservations)); }

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        if (res.CheckOutDate == null || res.CheckOutDate.Value.DayNumber - today.DayNumber <= 0) { TempData["ErrorMessage"] = "No days remain."; return RedirectToAction(nameof(Reservations)); }

        var reducedNights = res.CheckOutDate.Value.DayNumber - today.DayNumber;
        var roomPrice = res.ReservationRooms.FirstOrDefault()?.Room?.RoomType?.BasePrice ?? 0m;
        var refundTax = tax.CalculateTaxes(roomPrice * reducedNights, companyId);

        res.CheckOutDate = today; res.Status = "Checked Out";
        res.Subtotal = Math.Max(0m, (res.Subtotal ?? 0m) - refundTax.Subtotal);
        res.TaxAmount = Math.Max(0m, (res.TaxAmount ?? 0m) - refundTax.TaxAmount);
        res.ServiceCharge = Math.Max(0m, (res.ServiceCharge ?? 0m) - refundTax.ServiceCharge);
        res.TotalAmount = Math.Max(0m, (res.TotalAmount ?? 0m) - refundTax.Total);

        foreach (var rr in res.ReservationRooms) if (rr.Room != null) rr.Room.Status = "Available";

        if (refundTax.Total > 0m)
            _db.Transactions.Add(new AccountingTransaction { CompanyId = companyId, Subtotal = -refundTax.Subtotal, TaxAmount = -refundTax.TaxAmount,
                ServiceCharge = -refundTax.ServiceCharge, Amount = -refundTax.Total, Type = "Refund",
                Description = $"Early Check-out Refund ({reducedNights} nights) Res #{res.ReservationId}",
                ReferenceId = res.ReservationId, ReferenceType = "Reservation", TransactionDate = ViaReservaERP.AppTime.Now });

        _db.AuditLogs.Add(new AuditLog { CompanyId = companyId, UserId = CurrentUserId, Action = "Early Check-out", TableName = "Reservations",
            RecordId = res.ReservationId, NewValues = $"Admin early checkout. {reducedNights} nights refunded ₱{refundTax.Total:N2}",
            ActionDate = ViaReservaERP.AppTime.Now });

        var guestUserId3 = res.Guest?.UserId;
        if (guestUserId3.HasValue)
            _db.Notifications.Add(new Notification { UserId = guestUserId3.Value, CompanyId = companyId,
                Title = "Early Check-out", Message = $"You have been checked out early from reservation #{res.ReservationId}. Refund: ₱{refundTax.Total:N2}.",
                Type = "Reservation", IsRead = false, CreatedAt = ViaReservaERP.AppTime.Now });

        await _db.SaveChangesAsync(ct);
        await _notify.NotifyRoleAsync(companyId, 3, "Early Check-out Refund", $"Reservation #{res.ReservationId} early checkout. Refund: ₱{refundTax.Total:N2}.", "Reservation", ct);
        await _notify.NotifyRoleAsync(companyId, 4, "Early Check-out", $"Reservation #{res.ReservationId} checked out early by admin.", "Reservation", ct);

        TempData["SuccessMessage"] = $"Reservation #{res.ReservationId} checked out early. Refund: ₱{refundTax.Total:N2}.";
        return RedirectToAction(nameof(Reservations));
    }

    public async Task<IActionResult> ExportReservations(string format = "csv", string? searchGuest = null, string? statusFilter = null, DateOnly? startDate = null, DateOnly? endDate = null, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        format = (format ?? "csv").Trim().ToLowerInvariant();

        var query = _db.Reservations
            .Include(r => r.Guest)
            .Include(r => r.ReservationRooms)
                .ThenInclude(rr => rr.Room)
            .Where(r => r.CompanyId == companyId);

        if (!string.IsNullOrEmpty(searchGuest))
            query = query.Where(r => r.Guest.FullName.Contains(searchGuest) || r.Guest.Email.Contains(searchGuest));
        
        if (!string.IsNullOrEmpty(statusFilter))
            query = query.Where(r => r.Status == statusFilter);

        if (startDate.HasValue)
            query = query.Where(r => r.CheckInDate >= startDate.Value);

        if (endDate.HasValue)
            query = query.Where(r => r.CheckOutDate <= endDate.Value);

        var rows = await query.OrderByDescending(r => r.ReservationId).ToListAsync(ct);

        var headers = new[] { "ID", "Guest", "Room(s)", "Check-In", "Check-Out", "Status", "Amount" };
        var data = rows.Select(r => new[]
        {
            $"#{r.ReservationId}",
            r.Guest?.FullName ?? "Unknown",
            string.Join(", ", r.ReservationRooms.Select(rr => rr.Room?.RoomNumber)),
            r.CheckInDate?.ToShortDateString() ?? "",
            r.CheckOutDate?.ToShortDateString() ?? "",
            r.Status ?? "Pending",
            (r.TotalAmount ?? 0m).ToString("N2", CultureInfo.InvariantCulture)
        });

        var kpis = new Dictionary<string, string>
        {
            ["Total Rows"] = rows.Count.ToString(CultureInfo.InvariantCulture),
            ["Total Revenue"] = rows.Sum(r => r.TotalAmount ?? 0m).ToString("N2", CultureInfo.InvariantCulture),
            ["Status Filter"] = statusFilter ?? "All"
        };

        return format switch
        {
            "xlsx" => ExportExcel("Reservations", headers, data),
            "pdf" => ExportPdf("Reservations", headers, data, kpis),
            _ => ExportCsv("Reservations", headers, data)
        };
    }

    private IActionResult ExportCsv(string title, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        var sb = new StringBuilder();
        sb.AppendLine(string.Join(',', headers.Select(EscapeCsv)));
        foreach (var r in rows)
            sb.AppendLine(string.Join(',', r.Select(EscapeCsv)));

        var bytes = Encoding.UTF8.GetBytes(sb.ToString());
        return File(bytes, "text/csv", FileName(title, "csv"));
    }

    private IActionResult ExportExcel(string title, IReadOnlyList<string> headers, IEnumerable<string[]> rows)
    {
        using var wb = new XLWorkbook();
        var ws = wb.Worksheets.Add("Report");

        for (var i = 0; i < headers.Count; i++)
            ws.Cell(1, i + 1).Value = headers[i];

        ws.Range(1, 1, 1, headers.Count).Style.Font.Bold = true;
        ws.Range(1, 1, 1, headers.Count).Style.Fill.BackgroundColor = XLColor.FromHtml("#f1f5f9");

        var rowIndex = 2;
        foreach (var r in rows)
        {
            for (var c = 0; c < headers.Count && c < r.Length; c++)
                ws.Cell(rowIndex, c + 1).Value = r[c];
            rowIndex++;
        }

        ws.Columns().AdjustToContents();

        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", FileName(title, "xlsx"));
    }

    private IActionResult ExportPdf(string title, IReadOnlyList<string> headers, IEnumerable<string[]> rows, IReadOnlyDictionary<string, string> kpis)
    {
        var logoPath = Path.Combine(_env.WebRootPath, "img", "viareserva-logo.png");
        byte[]? logoBytes = System.IO.File.Exists(logoPath) ? System.IO.File.ReadAllBytes(logoPath) : null;

        var created = ViaReservaERP.AppTime.Now;
        var rowList = rows.Take(2000).ToList();

        var doc = QuestPDF.Fluent.Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(28);
                page.DefaultTextStyle(x => x.FontSize(10).FontColor(Colors.Grey.Darken3));

                page.Header().Row(row =>
                {
                    row.RelativeItem().Row(left =>
                    {
                        if (logoBytes != null)
                            left.ConstantItem(54).Height(24).Image(logoBytes);

                        left.RelativeItem().Column(col =>
                        {
                            col.Item().Text("ViaReservaERP").FontSize(12).FontColor("#1a2a6c").SemiBold();
                            col.Item().Text("Hospitality Enterprise Platform").FontSize(9).FontColor(Colors.Grey.Darken2);
                        });
                    });

                    row.ConstantItem(200).AlignRight().Column(col =>
                    {
                        col.Item().Text(title).FontSize(14).FontColor("#1e293b").SemiBold();
                        col.Item().Text($"Generated: {FormatPhilippinesTime(created)}").FontSize(9).FontColor(Colors.Grey.Darken2);
                    });
                });

                page.Content().Column(col =>
                {
                    col.Spacing(10);

                    if (kpis.Count > 0)
                    {
                        col.Item().Row(r =>
                        {
                            foreach (var kv in kpis)
                            {
                                r.RelativeItem().Border(1).BorderColor("#e2e8f0").Background("#f8fafc").Padding(10).Column(c =>
                                {
                                    c.Item().Text(kv.Key).FontSize(9).FontColor(Colors.Grey.Darken2);
                                    c.Item().Text(kv.Value).FontSize(12).FontColor("#1e293b").SemiBold();
                                });
                            }
                        });
                    }

                    col.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            for (var i = 0; i < headers.Count; i++)
                                columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            for (var i = 0; i < headers.Count; i++)
                            {
                                header.Cell().Background("#f1f5f9").BorderBottom(1).BorderColor("#e2e8f0")
                                    .PaddingVertical(6).PaddingHorizontal(6)
                                    .Text(headers[i]).FontColor("#1e293b").SemiBold().FontSize(9);
                            }
                        });

                        foreach (var r in rowList)
                        {
                            for (var i = 0; i < headers.Count; i++)
                            {
                                var val = i < r.Length ? r[i] : "";
                                table.Cell().BorderBottom(1).BorderColor("#f1f5f9").PaddingVertical(6).PaddingHorizontal(6)
                                    .Text(val).FontSize(9);
                            }
                        }
                    });

                    col.Item().Text($"Rows: {rowList.Count:N0}").FontSize(9).FontColor(Colors.Grey.Darken2);
                });

                page.Footer().AlignCenter().Text(x =>
                {
                    x.Span("Page ");
                    x.CurrentPageNumber();
                    x.Span(" / ");
                    x.TotalPages();
                });
            });
        });

        using var ms = new MemoryStream();
        doc.GeneratePdf(ms);
        return File(ms.ToArray(), "application/pdf", FileName(title, "pdf"));
    }

    private static string EscapeCsv(string input)
    {
        var s = input ?? string.Empty;
        if (s.Contains('"'))
            s = s.Replace("\"", "\"\"");
        if (s.Contains(',') || s.Contains('\n') || s.Contains('\r') || s.Contains('"'))
            s = $"\"{s}\"";
        return s;
    }

    private static string FileName(string title, string ext)
    {
        var safe = new string((title ?? "Export").Where(ch => char.IsLetterOrDigit(ch) || ch == ' ' || ch == '-' || ch == '_').ToArray()).Trim();
        safe = string.Join('-', safe.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        return $"{safe}-{ViaReservaERP.AppTime.Now:yyyyMMdd-HHmm}.{ext}";
    }

    private static string FormatPhilippinesTime(DateTime dt)
    {
        var utc = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
        var ph = TimeZoneInfo.ConvertTimeFromUtc(utc, PhTimeZone);
        return ph.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture);
    }



    public async Task<IActionResult> Rooms(string? search, string sort = "number", string dir = "asc", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        await _checkout.SyncRoomStatusesAsync(companyId, ct);
        
        var baseQuery = _db.Rooms
            .Include(r => r.RoomType)
            .Where(r => r.CompanyId == companyId && !r.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            baseQuery = baseQuery.Where(r => r.RoomNumber.Contains(term));
        }

        var asc = !string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        var q = sort?.ToLowerInvariant() switch
        {
            "type" => asc ? baseQuery.OrderBy(r => r.RoomType != null ? r.RoomType.TypeName : "") : baseQuery.OrderByDescending(r => r.RoomType != null ? r.RoomType.TypeName : ""),
            "status" => asc ? baseQuery.OrderBy(r => r.Status) : baseQuery.OrderByDescending(r => r.Status),
            "price" => asc ? baseQuery.OrderBy(r => r.RoomType != null ? r.RoomType.BasePrice : 0) : baseQuery.OrderByDescending(r => r.RoomType != null ? r.RoomType.BasePrice : 0),
            _ => asc ? baseQuery.OrderBy(r => r.RoomNumber) : baseQuery.OrderByDescending(r => r.RoomNumber)
        };

        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;

        var totalRows = await q.CountAsync(ct);
        var rooms = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        var roomIds = rooms.Select(r => r.RoomId).ToList();

        // Fetch active reservations for these rooms to identify current occupants
        var activeReservations = await _db.ReservationRooms
            .Include(rr => rr.Reservation)
                .ThenInclude(res => res!.Guest)
            .Where(rr => rr.RoomId.HasValue && roomIds.Contains(rr.RoomId.Value))
            .Where(rr => rr.Reservation!.Status != "Cancelled" && rr.Reservation.Status != "Completed")
            .Where(rr => rr.Reservation.CheckInDate <= today && rr.Reservation.CheckOutDate >= today)
            .ToListAsync(ct);

        var rows = rooms.Select(r => 
        {
            var occupantRes = activeReservations
                .Where(ar => ar.RoomId == r.RoomId)
                .OrderByDescending(ar => ar.Reservation?.Status == "Checked In" ? 1 : 0)
                .ThenByDescending(ar => ar.ReservationId)
                .FirstOrDefault();

            return new InventoryRowViewModel
            {
                Room = r,
                CurrentOccupant = occupantRes?.Reservation?.Guest?.FullName,
                CurrentReservationId = occupantRes?.ReservationId
            };
        }).ToList();

        var types = await _db.RoomTypes
            .Where(rt => rt.CompanyId == companyId)
            .ToListAsync(ct);

        var model = new Models.Admin.InventoryViewModel
        {
            Search = search,
            Rows = rows,
            RoomTypes = types,
            TotalAvailable = await baseQuery.CountAsync(r => r.Status == "Available", ct),
            TotalOccupied = await baseQuery.CountAsync(r => r.Status == "Occupied", ct),
            TotalMaintenance = await baseQuery.CountAsync(r => r.Status == "Maintenance", ct),
            TotalDirty = await baseQuery.CountAsync(r => r.Status == "Dirty", ct),
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            Sort = sort,
            Dir = dir
        };

        ViewData["Title"] = "Room Management";
        return View(model);
    }

    public async Task<IActionResult> RoomTypes(CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var types = await _db.RoomTypes
            .Where(rt => rt.CompanyId == companyId)
            .ToListAsync(ct);

        ViewData["Title"] = "Room Type Management";
        return View(types);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRoomType(string typeName, decimal? basePrice, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        if (string.IsNullOrWhiteSpace(typeName))
        {
            TempData["ErrorMessage"] = "Room type name is required.";
            return RedirectToAction(nameof(Rooms));
        }

        var exists = await _db.RoomTypes.AnyAsync(rt => rt.CompanyId == companyId && rt.TypeName == typeName, ct);
        if (exists)
        {
            TempData["ErrorMessage"] = "Room type already exists.";
            return RedirectToAction(nameof(Rooms));
        }

        _db.RoomTypes.Add(new RoomType
        {
            CompanyId = companyId,
            TypeName = typeName.Trim(),
            BasePrice = basePrice ?? 0m
        });

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Insert",
            TableName = "RoomTypes",
            RecordId = 0,
            NewValues = $"RoomType created: {typeName} @ {(basePrice ?? 0m):N2}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Room type created successfully.";
        return RedirectToAction(nameof(Rooms));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateRoom([FromForm] RoomCreateInput input, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Invalid room input.";
            return RedirectToAction(nameof(Rooms));
        }

        // Plan Limit Enforcement
        var sub = await _db.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.CompanyId == companyId && s.Status == "Active")
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefaultAsync(ct);

        if (sub?.Plan?.PlanName == "Basic")
        {
            var currentRoomCount = await _db.Rooms.CountAsync(r => r.CompanyId == companyId && !r.IsDeleted, ct);
            if (currentRoomCount >= 25)
            {
                TempData["ErrorMessage"] = "You have reached the limit of 25 rooms for the Basic Plan. Please upgrade your subscription to add more rooms.";
                return RedirectToAction(nameof(Rooms));
            }
        }

        if (string.IsNullOrWhiteSpace(input.RoomNumber))
        {
            TempData["ErrorMessage"] = "Room number is required.";
            return RedirectToAction(nameof(Rooms));
        }

        var type = await _db.RoomTypes.FirstOrDefaultAsync(rt => rt.RoomTypeId == input.RoomTypeId && rt.CompanyId == companyId, ct);
        if (type == null)
        {
            TempData["ErrorMessage"] = "Invalid room type.";
            return RedirectToAction(nameof(Rooms));
        }

        var roomNumber = input.RoomNumber.Trim();
        var exists = await _db.Rooms.AnyAsync(r => r.CompanyId == companyId && r.RoomNumber == roomNumber, ct);
        if (exists)
        {
            TempData["ErrorMessage"] = "Room number already exists.";
            return RedirectToAction(nameof(Rooms));
        }

        _db.Rooms.Add(new Room
        {
            CompanyId = companyId,
            RoomTypeId = input.RoomTypeId,
            RoomNumber = roomNumber,
            Status = "Available"
        });

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Insert",
            TableName = "Rooms",
            RecordId = 0,
            NewValues = $"Room created: {roomNumber}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Room created successfully.";
        return RedirectToAction(nameof(Rooms));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRoom(int roomId, string roomNumber, int roomTypeId, string status, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.RoomId == roomId && r.CompanyId == companyId, ct);
        if (room == null) return NotFound();

        if (string.IsNullOrWhiteSpace(roomNumber))
        {
            TempData["ErrorMessage"] = "Room number is required.";
            return RedirectToAction(nameof(Rooms));
        }

        var typeOk = await _db.RoomTypes.AnyAsync(rt => rt.RoomTypeId == roomTypeId && rt.CompanyId == companyId, ct);
        if (!typeOk)
        {
            TempData["ErrorMessage"] = "Invalid room type.";
            return RedirectToAction(nameof(Rooms));
        }

        var dup = await _db.Rooms.AnyAsync(r => r.CompanyId == companyId && r.RoomId != roomId && r.RoomNumber == roomNumber, ct);
        if (dup)
        {
            TempData["ErrorMessage"] = "Room number already exists.";
            return RedirectToAction(nameof(Rooms));
        }

        var old = $"RoomNumber={room.RoomNumber};TypeId={room.RoomTypeId};Status={room.Status}";

        room.RoomNumber = roomNumber.Trim();
        room.RoomTypeId = roomTypeId;
        room.Status = string.IsNullOrWhiteSpace(status) ? room.Status : status.Trim();

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Update",
            TableName = "Rooms",
            RecordId = roomId,
            OldValues = old,
            NewValues = $"RoomNumber={room.RoomNumber};TypeId={room.RoomTypeId};Status={room.Status}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Room updated successfully.";
        return RedirectToAction(nameof(Rooms));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateRoomStatus(int id, string status, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.RoomId == id && r.CompanyId == companyId, ct);
        if (room == null) return NotFound();

        if (string.IsNullOrWhiteSpace(status))
        {
            TempData["ErrorMessage"] = "Status is required.";
            return RedirectToAction(nameof(Rooms));
        }

        var oldStatus = room.Status;
        room.Status = status.Trim();

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Update",
            TableName = "Rooms",
            RecordId = id,
            OldValues = oldStatus,
            NewValues = room.Status,
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Room status updated.";
        return RedirectToAction(nameof(Rooms));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteRoom(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var room = await _db.Rooms
            .Include(r => r.RoomType)
            .FirstOrDefaultAsync(r => r.RoomId == id && r.CompanyId == companyId, ct);

        if (room == null) return NotFound();

        var snapshot = $"RoomNumber={room.RoomNumber};Type={(room.RoomType?.TypeName ?? room.RoomTypeId.ToString())};Status={room.Status}";

        room.IsDeleted = true;

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Archive",
            TableName = "Rooms",
            RecordId = id,
            OldValues = snapshot,
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Room archived successfully.";
        return RedirectToAction(nameof(Rooms));
    }

    [HttpGet]
    [Authorize(Policy = RoleNames.CompanyAdmin)]
    public async Task<IActionResult> ArchivedRooms(string sort = "number", string dir = "asc", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        
        var baseQuery = _db.Rooms
            .Include(r => r.RoomType)
            .Where(r => r.CompanyId == companyId && r.IsDeleted);

        var asc = !string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        var q = sort?.ToLowerInvariant() switch
        {
            "type" => asc ? baseQuery.OrderBy(r => r.RoomType != null ? r.RoomType.TypeName : "") : baseQuery.OrderByDescending(r => r.RoomType != null ? r.RoomType.TypeName : ""),
            "status" => asc ? baseQuery.OrderBy(r => r.Status) : baseQuery.OrderByDescending(r => r.Status),
            _ => asc ? baseQuery.OrderBy(r => r.RoomNumber) : baseQuery.OrderByDescending(r => r.RoomNumber)
        };

        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;

        var totalRows = await q.CountAsync(ct);
        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var model = new Models.Admin.InventoryViewModel
        {
            Rows = rows.Select(r => new InventoryRowViewModel { Room = r }).ToList(),
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            Sort = sort,
            Dir = dir
        };

        ViewData["Title"] = "Archived Rooms";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreRoom(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var room = await _db.Rooms.FirstOrDefaultAsync(r => r.RoomId == id && r.CompanyId == companyId && r.IsDeleted, ct);
        if (room == null) return NotFound();

        room.IsDeleted = false;

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Restore",
            TableName = "Rooms",
            RecordId = id,
            NewValues = $"Room restored: {room.RoomNumber}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Room restored successfully.";
        return RedirectToAction(nameof(ArchivedRooms));
    }

    [Authorize(Policy = RoleNames.CompanyAdmin)]
    public async Task<IActionResult> Accounting(CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var firstDayOfMonth = new DateTime(ViaReservaERP.AppTime.Now.Date.Year, ViaReservaERP.AppTime.Now.Date.Month, 1);

        var transactions = await _db.Transactions
            .Where(t => t.CompanyId == companyId)
            .OrderByDescending(t => t.TransactionDate)
            .Take(10)
            .ToListAsync(ct);

        var payments = await _db.Payments
            .Where(p => p.CompanyId == companyId)
            .OrderByDescending(p => p.CreatedAt)
            .Take(10)
            .ToListAsync(ct);

        var totalRev = await _db.Payments
            .Where(p => p.CompanyId == companyId && p.Status == "Succeeded")
            .SumAsync(p => p.Amount.HasValue ? p.Amount.Value : 0m, ct);

        var model = new Models.Admin.AccountingDashboardViewModel
        {
            TotalRevenue = totalRev,
            RecentTransactions = transactions,
            RecentPayments = payments
        };

        ViewData["Title"] = "Company Accounting";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        
        var guests = await _db.Guests
            .Where(g => g.CompanyId == companyId && !g.IsDeleted)
            .OrderBy(g => g.FullName)
            .Select(g => new SelectListItem { Value = g.GuestId.ToString(), Text = g.FullName })
            .ToListAsync(ct);

        var rooms = await _db.Rooms
            .Include(r => r.RoomType)
            .Where(r => r.CompanyId == companyId && r.Status == "Available")
            .Select(r => new Models.Admin.RoomSelectionItem
            {
                RoomId = r.RoomId,
                RoomNumber = r.RoomNumber != null ? r.RoomNumber : "N/A",
                TypeName = r.RoomType != null ? r.RoomType.TypeName : "Standard",
                BasePrice = r.RoomType != null && r.RoomType.BasePrice.HasValue ? r.RoomType.BasePrice.Value : 0m
            })
            .ToListAsync(ct);

        var model = new Models.Admin.CreateReservationViewModel
        {
            Guests = guests,
            AvailableRooms = rooms
        };

        ViewData["Title"] = "Create Reservation";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Models.Admin.CreateReservationViewModel model, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        if (model.SelectedGuestId == null && string.IsNullOrEmpty(model.NewGuestFullName))
        {
            ModelState.AddModelError("", "Please select a guest or enter new guest details.");
        }

        if (ModelState.IsValid)
        {
            using var transaction = await _db.Database.BeginTransactionAsync(ct);
            try
            {
                int guestId;
                if (model.SelectedGuestId.HasValue)
                {
                    guestId = model.SelectedGuestId.Value;
                }
                else
                {
                    var newGuest = new Guest
                    {
                        CompanyId = companyId,
                        FullName = model.NewGuestFullName,
                        Email = model.NewGuestEmail,
                        Phone = model.NewGuestPhone,
                        CreatedAt = ViaReservaERP.AppTime.Now
                    };
                    _db.Guests.Add(newGuest);
                    await _db.SaveChangesAsync(ct);
                    guestId = newGuest.GuestId;
                }

                var room = await _db.Rooms.Include(r => r.RoomType).FirstOrDefaultAsync(r => r.RoomId == model.SelectedRoomId, ct);
                if (room == null || room.Status != "Available")
                {
                    TempData["ErrorMessage"] = "The selected room is no longer available.";
                    return RedirectToAction(nameof(Create));
                }

                // Compute total amount
                var nights = Math.Max(1, (model.CheckOutDate.ToDateTime(TimeOnly.MinValue) - model.CheckInDate.ToDateTime(TimeOnly.MinValue)).Days);
                var basePrice = room.RoomType?.BasePrice ?? 0m;
                var totalAmount = basePrice * nights;

                var reservation = new Reservation
                {
                    CompanyId = companyId,
                    GuestId = guestId,
                    CheckInDate = model.CheckInDate,
                    CheckOutDate = model.CheckOutDate,
                    Status = "Pending",
                    TotalAmount = totalAmount
                };
                _db.Reservations.Add(reservation);
                await _db.SaveChangesAsync(ct);

                var resRoom = new ReservationRoom
                {
                    ReservationId = reservation.ReservationId,
                    RoomId = room.RoomId,
                    Price = basePrice
                };
                _db.ReservationRooms.Add(resRoom);

                // --- Workflow Integration ---
                var workflow = await _db.Workflows.FirstOrDefaultAsync(w => w.CompanyId == companyId && w.Name == "Reservation Approval", ct);
                if (workflow == null)
                {
                    workflow = new Workflow { CompanyId = companyId, Name = "Reservation Approval", Description = "Standard approval flow for new reservations" };
                    _db.Workflows.Add(workflow);
                    await _db.SaveChangesAsync(ct);
                    
                    _db.WorkflowSteps.Add(new WorkflowStep { WorkflowId = workflow.WorkflowId, StepOrder = 1, RoleId = 2, ActionName = "Manager Review" });
                }

                var wfInstance = new WorkflowInstance
                {
                    WorkflowId = workflow.WorkflowId,
                    CompanyId = companyId,
                    ReferenceId = reservation.ReservationId,
                    ReferenceType = "Reservation",
                    CurrentStep = 1,
                    Status = "Pending",
                    CreatedAt = ViaReservaERP.AppTime.Now
                };
                _db.WorkflowInstances.Add(wfInstance);

                _db.AuditLogs.Add(new AuditLog
                {
                    CompanyId = companyId,
                    UserId = userId,
                    Action = "Insert",
                    TableName = "Reservations",
                    RecordId = reservation.ReservationId,
                    NewValues = $"Reservation created and Workflow started for Guest ID {guestId}",
                    ActionDate = ViaReservaERP.AppTime.Now
                });

                _db.Notifications.Add(new Notification
                {
                    UserId = userId ?? 0,
                    CompanyId = companyId,
                    Title = "Approval Required",
                    Message = $"Reservation #{reservation.ReservationId} is pending approval.",
                    Type = "Workflow",
                    CreatedAt = ViaReservaERP.AppTime.Now
                });

                await _db.SaveChangesAsync(ct);
                await transaction.CommitAsync(ct);

                TempData["SuccessMessage"] = "Reservation created successfully.";
                return RedirectToAction(nameof(Reservations));
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(ct);
                ModelState.AddModelError("", "An error occurred while creating the reservation.");
            }
        }

        var guests = await _db.Guests.Where(g => g.CompanyId == companyId && !g.IsDeleted).Select(g => new SelectListItem { Value = g.GuestId.ToString(), Text = g.FullName }).ToListAsync(ct);
        model.Guests = guests;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> AssignRoom(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var res = await _db.Reservations
            .Include(r => r.Guest)
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room)
            .FirstOrDefaultAsync(r => r.ReservationId == id && r.CompanyId == companyId, ct);

        if (res == null) return NotFound();

        var currentRoom = res.ReservationRooms.FirstOrDefault();
        
        // Find rooms available for this reservation's dates
        var checkIn = res.CheckInDate ?? DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);
        var checkOut = res.CheckOutDate ?? DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date.AddDays(1));

        var busyRooms = await _db.ReservationRooms
            .Include(rr => rr.Reservation)
            .Where(rr => rr.Reservation.CompanyId == companyId && 
                         rr.Reservation.ReservationId != id &&
                         rr.Reservation.Status != "Cancelled" &&
                         rr.Reservation.CheckInDate < checkOut && 
                         rr.Reservation.CheckOutDate > checkIn)
            .Select(rr => rr.RoomId)
            .ToListAsync(ct);

        var availableRooms = await _db.Rooms
            .Include(r => r.RoomType)
            .Where(r => r.CompanyId == companyId && !busyRooms.Contains(r.RoomId))
            .Select(r => new Models.Admin.AvailableRoomItem
            {
                RoomId = r.RoomId,
                RoomNumber = r.RoomNumber != null ? r.RoomNumber : "N/A",
                TypeName = r.RoomType != null ? r.RoomType.TypeName : "Standard",
                Price = r.RoomType != null && r.RoomType.BasePrice.HasValue ? r.RoomType.BasePrice.Value : 0m,
                IsUpgrade = currentRoom != null && r.RoomType != null && r.RoomType.BasePrice > (currentRoom.Room != null && currentRoom.Room.RoomType != null ? currentRoom.Room.RoomType.BasePrice : 0m)
            })
            .ToListAsync(ct);

        var model = new Models.Admin.RoomAssignmentViewModel
        {
            ReservationId = id,
            GuestName = res.Guest?.FullName ?? "Unknown",
            CheckInDate = checkIn,
            CheckOutDate = checkOut,
            CurrentRoomId = currentRoom?.RoomId,
            CurrentRoomNumber = currentRoom?.Room?.RoomNumber ?? "None",
            AvailableRooms = availableRooms
        };

        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessRoomAssignment(Models.Admin.RoomAssignmentViewModel model, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var res = await _db.Reservations
            .Include(r => r.ReservationRooms)
            .FirstOrDefaultAsync(r => r.ReservationId == model.ReservationId && r.CompanyId == companyId, ct);

        if (res == null) return NotFound();

        var room = await _db.Rooms.Include(r => r.RoomType).FirstOrDefaultAsync(r => r.RoomId == model.SelectedRoomId && r.CompanyId == companyId, ct);
        if (room == null)
        {
            TempData["ErrorMessage"] = "Selected room is invalid.";
            return RedirectToAction(nameof(AssignRoom), new { id = model.ReservationId });
        }

        // Remove old assignment
        var oldAssignments = _db.ReservationRooms.Where(rr => rr.ReservationId == model.ReservationId);
        _db.ReservationRooms.RemoveRange(oldAssignments);

        // Add new assignment
        var newAssignment = new ReservationRoom
        {
            ReservationId = model.ReservationId,
            RoomId = room.RoomId,
            Price = room.RoomType?.BasePrice ?? 0m
        };
        _db.ReservationRooms.Add(newAssignment);

        // Update reservation total if it's an upgrade/transfer
        var nights = Math.Max(1, (res.CheckOutDate.Value.ToDateTime(TimeOnly.MinValue) - res.CheckInDate.Value.ToDateTime(TimeOnly.MinValue)).Days);
        res.TotalAmount = (room.RoomType?.BasePrice ?? 0m) * nights;

        // Audit Log
        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Update",
            TableName = "Reservations",
            RecordId = res.ReservationId,
            NewValues = $"Room changed to {room.RoomNumber}. New Total: {res.TotalAmount}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Room assigned successfully.";
        return RedirectToAction(nameof(Details), new { id = model.ReservationId });
    }

    [Authorize(Policy = RoleNames.CompanyAdmin)]
    public async Task<IActionResult> Staff(string? search, string sort = "name", string dir = "asc", int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var today = ViaReservaERP.AppTime.Now;
        var firstDayOfMonth = new DateTime(today.Year, today.Month, 1);

        var baseQuery = _db.Users
            .Include(u => u.Role)
            .Where(u => u.CompanyId == companyId && !u.IsDeleted)
            .Where(u => u.Role != null && u.Role.RoleName != RoleNames.Guest && u.Role.RoleName != RoleNames.SuperAdmin);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            baseQuery = baseQuery.Where(u => u.FullName.Contains(term) || u.Email.Contains(term));
        }

        var asc = !string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        var q = sort?.ToLowerInvariant() switch
        {
            "role" => asc ? baseQuery.OrderBy(u => u.Role.RoleName) : baseQuery.OrderByDescending(u => u.Role.RoleName),
            "date" => asc ? baseQuery.OrderBy(u => u.CreatedAt) : baseQuery.OrderByDescending(u => u.CreatedAt),
            "status" => asc ? baseQuery.OrderBy(u => u.IsActive) : baseQuery.OrderByDescending(u => u.IsActive),
            _ => asc ? baseQuery.OrderBy(u => u.FullName) : baseQuery.OrderByDescending(u => u.FullName)
        };

        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 10;

        var totalRows = await q.CountAsync(ct);
        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var roles = await _db.Roles
            .Where(r => r.RoleName != RoleNames.Guest && r.RoleName != RoleNames.SuperAdmin)
            .OrderBy(r => r.RoleName)
            .ToListAsync(ct);

        var model = new Models.Admin.StaffManagementViewModel
        {
            Search = search,
            Rows = rows,
            AvailableRoles = roles,
            TotalStaff = await baseQuery.CountAsync(ct),
            ActiveStaff = await baseQuery.CountAsync(u => u.IsActive, ct),
            RevokedStaff = await baseQuery.CountAsync(u => !u.IsActive, ct),
            NewStaffThisMonth = await baseQuery.CountAsync(u => u.CreatedAt >= firstDayOfMonth, ct),
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            Sort = sort,
            Dir = dir
        };

        ViewData["Title"] = "Staff Management";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateStaff(Models.Admin.StaffFormViewModel model, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        try
        {
            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Invalid staff input.";
                return RedirectToAction(nameof(Staff));
            }

            // Role validation
            var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleId == model.RoleId, ct);
            if (role == null)
            {
                TempData["ErrorMessage"] = "The selected role is invalid.";
                return RedirectToAction(nameof(Staff));
            }

            if (role.RoleName == RoleNames.SuperAdmin || role.RoleName == RoleNames.Guest)
            {
                TempData["ErrorMessage"] = "Unauthorized role selection.";
                return RedirectToAction(nameof(Staff));
            }

            var email = model.Email.Trim().ToLowerInvariant();
            var emailExists = await _db.Users.AnyAsync(u => u.Email == email && !u.IsDeleted, ct);
            if (emailExists)
            {
                TempData["ErrorMessage"] = $"The email '{model.Email}' is already registered in the system.";
                return RedirectToAction(nameof(Staff));
            }

            var newUser = new ErpUser
            {
                CompanyId = companyId,
                RoleId = model.RoleId,
                FullName = model.FullName.Trim(),
                Email = email,
                PasswordHash = PasswordHasher.Hash(model.Password),
                IsActive = true,
                CreatedAt = ViaReservaERP.AppTime.Now,
                IsDeleted = false
            };

            _db.Users.Add(newUser);

            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserId = userId,
                Action = "Insert",
                TableName = "Users",
                RecordId = 0,
                NewValues = $"Staff created: {newUser.Email} (Role: {role.RoleName})",
                ActionDate = ViaReservaERP.AppTime.Now
            });

            await _db.SaveChangesAsync(ct);
            TempData["SuccessMessage"] = $"Staff member {newUser.FullName} has been successfully added.";
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "An error occurred while creating the staff member: " + ex.Message;
        }

        return RedirectToAction(nameof(Staff));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleStaffActive(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var staff = await _db.Users.FirstOrDefaultAsync(u => u.UserId == id && u.CompanyId == companyId && u.RoleId != 6, ct);
        if (staff == null) return NotFound();

        staff.IsActive = !staff.IsActive;

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Update",
            TableName = "Users",
            RecordId = staff.UserId,
            NewValues = $"Staff active toggled: {staff.Email} => {staff.IsActive}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Staff status updated.";
        return RedirectToAction(nameof(Staff));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateStaff(Models.Admin.StaffEditFormViewModel model, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        if (model.UserId <= 0)
        {
            TempData["ErrorMessage"] = "Invalid staff member.";
            return RedirectToAction(nameof(Staff));
        }

        if (string.IsNullOrWhiteSpace(model.FullName))
        {
            TempData["ErrorMessage"] = "Full name is required.";
            return RedirectToAction(nameof(Staff));
        }

        if (userId.HasValue && model.UserId == userId.Value && !model.IsActive)
        {
            TempData["ErrorMessage"] = "You cannot disable your own account.";
            return RedirectToAction(nameof(Staff));
        }

        var role = await _db.Roles.FirstOrDefaultAsync(r => r.RoleId == model.RoleId, ct);
        if (role == null)
        {
            TempData["ErrorMessage"] = "Invalid role selection.";
            return RedirectToAction(nameof(Staff));
        }

        if (role.RoleName == RoleNames.SuperAdmin || role.RoleName == RoleNames.Guest)
        {
            TempData["ErrorMessage"] = "Unauthorized role selection.";
            return RedirectToAction(nameof(Staff));
        }

        var staff = await _db.Users
            .Include(u => u.Role)
            .FirstOrDefaultAsync(u => u.UserId == model.UserId && u.CompanyId == companyId && !u.IsDeleted && u.RoleId != 6, ct);

        if (staff == null) return NotFound();

        var oldValues = $"FullName={staff.FullName}; Role={(staff.Role != null ? staff.Role.RoleName : staff.RoleId.ToString())}; IsActive={staff.IsActive}";

        staff.FullName = model.FullName.Trim();
        staff.RoleId = model.RoleId;
        staff.IsActive = model.IsActive;
        staff.UpdatedAt = ViaReservaERP.AppTime.Now;

        var newValues = $"FullName={staff.FullName}; Role={role.RoleName}; IsActive={staff.IsActive}";

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Update",
            TableName = "Users",
            RecordId = staff.UserId,
            OldValues = oldValues,
            NewValues = newValues,
            IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers["User-Agent"].ToString(),
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Staff member updated.";
        return RedirectToAction(nameof(Staff));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteStaff(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        if (id == userId)
        {
            TempData["ErrorMessage"] = "You cannot archive yourself.";
            return RedirectToAction(nameof(Staff));
        }

        var staff = await _db.Users.FirstOrDefaultAsync(u => u.UserId == id && u.CompanyId == companyId && u.RoleId != 6, ct);
        if (staff == null) return NotFound();

        staff.IsDeleted = true;
        staff.IsActive = false;

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Archive",
            TableName = "Users",
            RecordId = id,
            NewValues = $"Staff archived: {staff.Email}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Staff member archived.";
        return RedirectToAction(nameof(Staff));
    }

    [HttpGet]
    public async Task<IActionResult> ArchivedStaff(string? search, string sort = "name", string dir = "asc", int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;

        var baseQuery = _db.Users.Include(u => u.Role).Where(u => u.CompanyId == companyId && u.RoleId != 6 && u.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            baseQuery = baseQuery.Where(u => u.FullName.Contains(term) || u.Email.Contains(term));
        }

        var asc = !string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        var q = sort?.ToLowerInvariant() switch
        {
            "role" => asc ? baseQuery.OrderBy(u => u.Role.RoleName) : baseQuery.OrderByDescending(u => u.Role.RoleName),
            "date" => asc ? baseQuery.OrderBy(u => u.CreatedAt) : baseQuery.OrderByDescending(u => u.CreatedAt),
            _ => asc ? baseQuery.OrderBy(u => u.FullName) : baseQuery.OrderByDescending(u => u.FullName)
        };

        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 10;

        var totalRows = await q.CountAsync(ct);
        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var model = new Models.Admin.StaffManagementViewModel
        {
            Search = search,
            Rows = rows,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            Sort = sort,
            Dir = dir
        };

        ViewData["Title"] = "Archived Staff";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreStaff(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var staff = await _db.Users.FirstOrDefaultAsync(u => u.UserId == id && u.CompanyId == companyId && u.RoleId != 6 && u.IsDeleted, ct);
        if (staff == null) return NotFound();

        staff.IsDeleted = false;

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Restore",
            TableName = "Users",
            RecordId = id,
            NewValues = $"Staff restored: {staff.Email}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Staff member restored successfully.";
        return RedirectToAction(nameof(ArchivedStaff));
    }

    public async Task<IActionResult> Guests(string? search = null, string sort = "created", string dir = "desc", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var today = ViaReservaERP.AppTime.Now;
        var firstDayOfMonth = new DateTime(today.Year, today.Month, 1);

        var query = _db.Guests.Where(g => g.CompanyId == companyId && !g.IsDeleted);
        
        if (!string.IsNullOrWhiteSpace(search))
        {
            query = query.Where(g => (g.FullName ?? "").Contains(search) || (g.Email ?? "").Contains(search) || (g.Phone ?? "").Contains(search));
        }

        var asc = !string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        var q = sort?.ToLowerInvariant() switch
        {
            "id" => asc ? query.OrderBy(g => g.GuestId) : query.OrderByDescending(g => g.GuestId),
            "created" => asc
                ? query.OrderBy(g => g.CreatedAt).ThenBy(g => g.GuestId)
                : query.OrderByDescending(g => g.CreatedAt).ThenByDescending(g => g.GuestId),
            "status" => asc ? query.OrderBy(g => g.IsActive) : query.OrderByDescending(g => g.IsActive),
            _ => asc ? query.OrderBy(g => g.FullName) : query.OrderByDescending(g => g.FullName)
        };

        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;

        var totalRows = await q.CountAsync(ct);
        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var model = new Models.Admin.GuestsViewModel
        {
            Rows = rows,
            TotalGuests = await _db.Guests.CountAsync(g => g.CompanyId == companyId && !g.IsDeleted, ct),
            ActiveGuests = await _db.Guests.CountAsync(g => g.CompanyId == companyId && !g.IsDeleted && g.IsActive, ct),
            InactiveGuests = await _db.Guests.CountAsync(g => g.CompanyId == companyId && !g.IsDeleted && !g.IsActive, ct),
            NewGuestsThisMonth = await _db.Guests.CountAsync(g => g.CompanyId == companyId && !g.IsDeleted && g.CreatedAt >= firstDayOfMonth, ct),
            Search = search,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            Sort = sort,
            Dir = dir
        };

        ViewData["Title"] = "Guest Management";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateGuest([FromForm] GuestCreateInput input, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Invalid guest input.";
            return RedirectToAction(nameof(Guests));
        }

        if (string.IsNullOrWhiteSpace(input.FullName))
        {
            TempData["ErrorMessage"] = "Full name is required.";
            return RedirectToAction(nameof(Guests));
        }

        var email = string.IsNullOrWhiteSpace(input.Email) ? null : input.Email.Trim();
        var phone = string.IsNullOrWhiteSpace(input.Phone) ? null : input.Phone.Trim();

        _db.Guests.Add(new Guest
        {
            CompanyId = companyId,
            FullName = input.FullName.Trim(),
            Email = email,
            Phone = phone,
            IsActive = true,
            IsDeleted = false,
            CreatedAt = ViaReservaERP.AppTime.Now
        });

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Insert",
            TableName = "Guests",
            RecordId = 0,
            NewValues = $"Guest created: {input.FullName} ({email})",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Guest created successfully.";
        return RedirectToAction(nameof(Guests));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ToggleGuestActive(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var guest = await _db.Guests.FirstOrDefaultAsync(g => g.GuestId == id && g.CompanyId == companyId && !g.IsDeleted, ct);
        if (guest == null) return NotFound();

        guest.IsActive = !guest.IsActive;

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Update",
            TableName = "Guests",
            RecordId = guest.GuestId,
            NewValues = $"Guest active toggled: {guest.Email} => {guest.IsActive}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Guest status updated.";
        return RedirectToAction(nameof(Guests));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteGuest(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var guest = await _db.Guests.FirstOrDefaultAsync(g => g.GuestId == id && g.CompanyId == companyId && !g.IsDeleted, ct);
        if (guest == null) return NotFound();

        guest.IsDeleted = true;
        guest.IsActive = false;

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Delete",
            TableName = "Guests",
            RecordId = guest.GuestId,
            NewValues = $"Guest soft-deleted: {guest.Email}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Guest deleted.";
        return RedirectToAction(nameof(Guests));
    }

    [HttpGet]
    public async Task<IActionResult> GuestDetails(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;

        var guest = await _db.Guests
            .FirstOrDefaultAsync(g => g.GuestId == id && g.CompanyId == companyId && !g.IsDeleted, ct);

        if (guest == null) return NotFound();

        var recentReservations = await _db.Reservations
            .Where(r => r.GuestId == id && r.CompanyId == companyId)
            .OrderByDescending(r => r.ReservationId)
            .Take(10)
            .ToListAsync(ct);

        var recentRequests = await _db.ServiceRequests
            .Include(sr => sr.Service)
            .Where(sr => sr.GuestId == id && sr.CompanyId == companyId)
            .OrderByDescending(sr => sr.RequestId)
            .Take(10)
            .ToListAsync(ct);

        var totalSpent = await _db.Payments
            .Include(p => p.Reservation)
            .Where(p => p.Reservation != null && p.Reservation.GuestId == id && p.Status == "Succeeded")
            .SumAsync(p => p.Amount ?? 0m, ct);

        var model = new Models.Admin.GuestDetailsViewModel
        {
            Guest = guest,
            RecentReservations = recentReservations,
            RecentRequests = recentRequests,
            TotalSpent = totalSpent,
            StayCount = recentReservations.Count(r => r.Status == "Checked Out" || r.Status == "Checked In"),
            LoyaltyTier = totalSpent > 50000 ? "Platinum" : totalSpent > 20000 ? "Gold" : "Standard"
        };

        ViewData["Title"] = "Guest Profile - " + guest.FullName;
        return View(model);
    }

    public async Task<IActionResult> Services(string? search, string? statusFilter, int cPage = 1, int rPage = 1, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var now = ViaReservaERP.AppTime.Now;
        var today = now.Date;
        var slaMinutes = 60;

        // Catalog Query
        var catalogQuery = _db.Services.Where(s => s.CompanyId == companyId && !s.IsDeleted);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            catalogQuery = catalogQuery.Where(s => s.ServiceName.Contains(term));
        }
        var catalogTotal = await catalogQuery.CountAsync(ct);
        var catalogRows = await catalogQuery
            .OrderBy(s => s.ServiceName)
            .Skip((cPage - 1) * 10)
            .Take(10)
            .ToListAsync(ct);

        // Requests Query
        var requestsQuery = _db.ServiceRequests
            .Include(sr => sr.Guest)
            .Include(sr => sr.Service)
            .Include(sr => sr.AssignedToUser)
            .Where(sr => sr.CompanyId == companyId);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            requestsQuery = requestsQuery.Where(sr => sr.Service.ServiceName.Contains(term) || sr.Guest.FullName.Contains(term));
        }

        if (!string.IsNullOrWhiteSpace(statusFilter))
        {
            requestsQuery = requestsQuery.Where(sr => sr.Status == statusFilter);
        }

        var requestTotal = await requestsQuery.CountAsync(ct);
        var requestRows = await requestsQuery
            .OrderByDescending(sr => sr.RequestDate)
            .Skip((rPage - 1) * 10)
            .Take(10)
            .ToListAsync(ct);

        var todayDo = DateOnly.FromDateTime(now);
        var requestGuestIds = requestRows
            .Where(r => r.GuestId.HasValue)
            .Select(r => r.GuestId!.Value)
            .Distinct()
            .ToList();

        var currentRoomByGuestId = new Dictionary<int, string>();
        if (requestGuestIds.Count > 0)
        {
            var guestRoomMap = await _db.ReservationRooms
                .AsNoTracking()
                .Include(rr => rr.Room)
                .Include(rr => rr.Reservation)
                .Where(rr => rr.Reservation != null && rr.Room != null)
                .Where(rr => rr.Reservation!.CompanyId == companyId)
                .Where(rr => requestGuestIds.Contains(rr.Reservation!.GuestId))
                .Where(rr => rr.Reservation!.Status != "Cancelled" && rr.Reservation.Status != "Completed")
                .Where(rr => rr.Reservation!.CheckInDate.HasValue && rr.Reservation.CheckOutDate.HasValue)
                .Where(rr => rr.Reservation!.CheckInDate!.Value <= todayDo && rr.Reservation.CheckOutDate!.Value >= todayDo)
                .OrderByDescending(rr => rr.Reservation!.Status == "Checked In" ? 1 : 0)
                .ThenByDescending(rr => rr.ReservationId)
                .Select(rr => new
                {
                    GuestId = rr.Reservation!.GuestId,
                    RoomNumber = rr.Room!.RoomNumber
                })
                .ToListAsync(ct);

            currentRoomByGuestId = guestRoomMap
                .GroupBy(x => x.GuestId)
                .ToDictionary(g => g.Key, g => g.FirstOrDefault()?.RoomNumber ?? "", comparer: EqualityComparer<int>.Default);
        }

        var staffOptions = await _db.Users
            .Where(u => u.CompanyId == companyId && u.RoleId == 5 && u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new SelectListItem { Value = u.UserId.ToString(), Text = (u.FullName ?? u.Email) })
            .ToListAsync(ct);

        var overdueCutoff = now.AddMinutes(-slaMinutes);

        var model = new Models.Admin.ServiceManagementViewModel
        {
            Search = search,
            CatalogRows = catalogRows,
            RequestRows = requestRows,
            StaffOptions = staffOptions,
            CurrentRoomByGuestId = currentRoomByGuestId,
            StatusFilter = statusFilter,
            
            CatalogPage = cPage,
            CatalogTotalRows = catalogTotal,
            RequestPage = rPage,
            RequestTotalRows = requestTotal,

            TotalCatalogItems = catalogTotal,
            PendingRequests = await _db.ServiceRequests.CountAsync(sr => sr.CompanyId == companyId && sr.Status == "Pending", ct),
            OverdueRequests = await _db.ServiceRequests.CountAsync(sr => sr.CompanyId == companyId && sr.Status != "Completed" && sr.RequestDate < overdueCutoff, ct),
            CompletedToday = await _db.ServiceRequests.CountAsync(sr => sr.CompanyId == companyId && sr.Status == "Completed" && sr.RequestDate >= today, ct)
        };

        ViewData["Title"] = "Service Management";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateService(Models.Admin.ServiceCatalogFormViewModel model, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Invalid service input.";
            return RedirectToAction(nameof(Services));
        }

        if (model.Price < 0)
        {
            TempData["ErrorMessage"] = "Price must be 0 or greater.";
            return RedirectToAction(nameof(Services));
        }

        _db.Services.Add(new ServiceCatalogItem
        {
            CompanyId = companyId,
            ServiceName = model.ServiceName.Trim(),
            Price = model.Price
        });

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Insert",
            TableName = "Services",
            RecordId = 0,
            NewValues = $"Service created: {model.ServiceName} @ {model.Price:N2}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Service created successfully.";
        return RedirectToAction(nameof(Services));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateService(Models.Admin.ServiceCatalogFormViewModel model, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault() ?? "Invalid service input.";
            return RedirectToAction(nameof(Services));
        }

        if (model.Price < 0)
        {
            TempData["ErrorMessage"] = "Price must be 0 or greater.";
            return RedirectToAction(nameof(Services));
        }

        if (!model.ServiceId.HasValue)
        {
            TempData["ErrorMessage"] = "Invalid service.";
            return RedirectToAction(nameof(Services));
        }

        var svc = await _db.Services.FirstOrDefaultAsync(s => s.ServiceId == model.ServiceId.Value && s.CompanyId == companyId, ct);
        if (svc == null) return NotFound();

        if (string.IsNullOrWhiteSpace(model.ServiceName))
        {
            TempData["ErrorMessage"] = "Service name is required.";
            return RedirectToAction(nameof(Services));
        }

        svc.ServiceName = model.ServiceName.Trim();
        svc.Price = model.Price;

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Update",
            TableName = "Services",
            RecordId = svc.ServiceId,
            NewValues = $"Service updated: {svc.ServiceName} @ {svc.Price:N2}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Service updated successfully.";
        return RedirectToAction(nameof(Services));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteService(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var svc = await _db.Services.FirstOrDefaultAsync(s => s.ServiceId == id && s.CompanyId == companyId, ct);
        if (svc == null) return NotFound();

        svc.IsDeleted = true;

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Archive",
            TableName = "Services",
            RecordId = id,
            NewValues = $"Service archived: {svc.ServiceName}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Service archived.";
        return RedirectToAction(nameof(Services));
    }

    [HttpGet]
    public async Task<IActionResult> ArchivedServices(string? search, int cPage = 1, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        
        var query = _db.Services.Where(s => s.CompanyId == companyId && s.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            query = query.Where(s => s.ServiceName.Contains(term));
        }

        var totalRows = await query.CountAsync(ct);
        var rows = await query
            .OrderBy(s => s.ServiceName)
            .Skip((cPage - 1) * 10)
            .Take(10)
            .ToListAsync(ct);

        var model = new Models.Admin.ServiceManagementViewModel
        {
            Search = search,
            CatalogRows = rows,
            CatalogPage = cPage,
            CatalogTotalRows = totalRows
        };

        ViewData["Title"] = "Archived Services";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RestoreService(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var svc = await _db.Services.FirstOrDefaultAsync(s => s.ServiceId == id && s.CompanyId == companyId && s.IsDeleted, ct);
        if (svc == null) return NotFound();

        svc.IsDeleted = false;

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = userId,
            Action = "Restore",
            TableName = "Services",
            RecordId = id,
            NewValues = $"Service restored: {svc.ServiceName}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Service restored successfully.";
        return RedirectToAction(nameof(ArchivedServices));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> AssignServiceRequest(int id, int assignedToUserId, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var exists = await _db.ServiceRequests.AnyAsync(sr => sr.RequestId == id && sr.CompanyId == companyId, ct);
        if (!exists) return NotFound();

        var staffOk = await _db.Users.AnyAsync(u => u.UserId == assignedToUserId && u.CompanyId == companyId && u.RoleId != 6 && u.IsActive, ct);
        if (!staffOk)
        {
            TempData["ErrorMessage"] = "Selected staff is invalid.";
            return RedirectToAction(nameof(Services));
        }

        await _serviceRequests.AssignAsync(id, assignedToUserId, performedByUserId: userId ?? 0, ct);
        TempData["SuccessMessage"] = "Service request assigned.";
        return RedirectToAction(nameof(Services));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateServiceRequestStatus(int id, string status, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var exists = await _db.ServiceRequests.AnyAsync(sr => sr.RequestId == id && sr.CompanyId == companyId, ct);
        if (!exists) return NotFound();

        await _serviceRequests.UpdateStatusAsync(id, status, performedByUserId: userId ?? 0, ct);
        TempData["SuccessMessage"] = "Service request status updated.";
        return RedirectToAction(nameof(Services));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EscalateServiceRequest(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var req = await _db.ServiceRequests.FirstOrDefaultAsync(sr => sr.RequestId == id && sr.CompanyId == companyId, ct);
        if (req == null) return NotFound();

        await _serviceRequests.UpdateStatusAsync(id, "Escalated", performedByUserId: userId ?? 0, ct);
        TempData["SuccessMessage"] = "Service request escalated.";
        return RedirectToAction(nameof(Approvals));
    }

    [Authorize(Policy = RoleNames.CompanyAdmin)]
    public async Task<IActionResult> Reports(DateOnly? startDate, DateOnly? endDate, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);
        var start = startDate ?? new DateOnly(today.Year, today.Month, 1);
        var end = endDate ?? new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        if (end < start) end = start;

        var startDt = start.ToDateTime(TimeOnly.MinValue);
        var endDt = end.ToDateTime(TimeOnly.MaxValue);

        var revenue = await _db.Payments
            .Where(p => p.CompanyId == companyId && p.Status == "Succeeded" && p.CreatedAt >= startDt && p.CreatedAt <= endDt)
            .SumAsync(p => p.Amount ?? 0m, ct);

        var txIncome = await _db.Transactions
            .Where(t => t.CompanyId == companyId && t.Type == "Income" && t.TransactionDate >= startDt && t.TransactionDate <= endDt)
            .SumAsync(t => t.Amount ?? 0m, ct);

        var txExpense = await _db.Transactions
            .Where(t => t.CompanyId == companyId && t.Type == "Expense" && t.TransactionDate >= startDt && t.TransactionDate <= endDt)
            .SumAsync(t => t.Amount ?? 0m, ct);

        var resCount = await _db.Reservations
            .Where(r => r.CompanyId == companyId && r.CheckInDate.HasValue && r.CheckInDate.Value >= start && r.CheckInDate.Value <= end)
            .CountAsync(ct);

        var serviceReqCount = await _db.ServiceRequests
            .Where(sr => sr.CompanyId == companyId && sr.RequestDate >= startDt && sr.RequestDate <= endDt)
            .CountAsync(ct);

        var model = new Models.Admin.ReportsViewModel
        {
            StartDate = start,
            EndDate = end,
            Revenue = revenue,
            Income = txIncome,
            Expense = txExpense,
            TotalReservations = resCount,
            TotalServiceRequests = serviceReqCount
        };

        // --- TREND CALCULATIONS ---
        var dailyRevenue = await _db.Payments
            .Where(p => p.CompanyId == companyId && p.Status == "Succeeded" && p.CreatedAt >= startDt && p.CreatedAt <= endDt)
            .GroupBy(p => p.CreatedAt.Date)
            .Select(g => new { Date = g.Key, Total = g.Sum(p => p.Amount ?? 0m) })
            .ToListAsync(ct);

        var dailyReservations = await _db.Reservations
            .Where(r => r.CompanyId == companyId && r.CheckInDate >= start && r.CheckInDate <= end)
            .GroupBy(r => r.CheckInDate)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var dailyServices = await _db.ServiceRequests
            .Where(sr => sr.CompanyId == companyId && sr.RequestDate >= startDt && sr.RequestDate <= endDt)
            .GroupBy(sr => sr.RequestDate.Date)
            .Select(g => new { Date = g.Key, Count = g.Count() })
            .ToListAsync(ct);

        var labels = new List<string>();
        var revData = new List<decimal>();
        var resData = new List<decimal>();
        var svcData = new List<decimal>();

        for (var d = start; d <= end; d = d.AddDays(1))
        {
            labels.Add(d.ToString("MMM dd"));
            var dt = d.ToDateTime(TimeOnly.MinValue).Date;
            revData.Add(dailyRevenue.FirstOrDefault(x => x.Date == dt)?.Total ?? 0m);
            resData.Add(dailyReservations.FirstOrDefault(x => x.Date == d)?.Count ?? 0);
            svcData.Add(dailyServices.FirstOrDefault(x => x.Date == dt)?.Count ?? 0);
        }

        model.RevenueAnalytics.Labels = labels;
        model.RevenueAnalytics.Datasets["Revenue"] = revData;

        model.ReservationAnalytics.Labels = labels;
        model.ReservationAnalytics.Datasets["Bookings"] = resData;

        model.ServiceAnalytics.Labels = labels;
        model.ServiceAnalytics.Datasets["Requests"] = svcData;

        // --- FORECASTS (Simple scaling) ---
        var daysInRange = (end.DayNumber - start.DayNumber) + 1;
        if (daysInRange > 0)
        {
            model.Forecast.ProjectedRevenue = (revenue / daysInRange) * 30;
            model.Forecast.ProjectedReservations = (int)((resCount / (decimal)daysInRange) * 30);
            model.Forecast.GrowthRate = 0.05m; // Baseline
        }

        ViewData["Title"] = "Financial Reports";
        return View(model);
    }

    public async Task<IActionResult> Details(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var res = await _db.Reservations
            .Include(r => r.Guest)
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room).ThenInclude(rm => rm.RoomType)
            .FirstOrDefaultAsync(r => r.ReservationId == id && r.CompanyId == companyId, ct);

        if (res == null) return NotFound();

        var wfInstance = await _db.WorkflowInstances
            .Include(w => w.Workflow)
            .FirstOrDefaultAsync(w => w.ReferenceId == id && w.ReferenceType == "Reservation" && w.CompanyId == companyId, ct);

        var history = wfInstance != null 
            ? await _db.WorkflowInstanceSteps
                .Include(s => s.PerformedByUser)
                .Where(s => s.InstanceId == wfInstance.InstanceId)
                .OrderByDescending(s => s.PerformedAt)
                .ToListAsync(ct)
            : new List<WorkflowInstanceStep>();

        var payment = await _db.Payments
            .OrderByDescending(p => p.PaymentId)
            .FirstOrDefaultAsync(p => p.ReservationId == id && p.CompanyId == companyId, ct);

        var model = new Models.Admin.ReservationDetailsViewModel
        {
            Reservation = res,
            WorkflowInstance = wfInstance,
            WorkflowHistory = history,
            CanApprove = wfInstance != null && wfInstance.Status == "Pending",
            Payment = payment
        };

        ViewData["Title"] = $"Reservation #{id}";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refund(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var payment = await _db.Payments
            .FirstOrDefaultAsync(p => p.ReservationId == id && p.CompanyId == companyId && p.Status == "Succeeded", ct);

        if (payment == null || string.IsNullOrWhiteSpace(payment.StripePaymentIntentId))
        {
            TempData["ErrorMessage"] = "No successful Stripe payment found for this reservation.";
            return RedirectToAction(nameof(Details), new { id });
        }

        try
        {
            var success = await _stripe.RefundPaymentAsync(payment.StripePaymentIntentId, ct);
            if (success)
            {
                payment.Status = "Refunded";
                await _db.SaveChangesAsync(ct);

                _db.AuditLogs.Add(new AuditLog
                {
                    CompanyId = companyId,
                    UserId = User.GetUserId(),
                    Action = "Refund",
                    TableName = "Payments",
                    RecordId = payment.PaymentId,
                    NewValues = $"Stripe Refund Processed: {payment.StripePaymentIntentId}",
                    ActionDate = ViaReservaERP.AppTime.Now
                });
                await _db.SaveChangesAsync(ct);

                TempData["SuccessMessage"] = "Payment has been refunded successfully via Stripe.";
            }
            else
            {
                TempData["ErrorMessage"] = "Stripe refund was not successful.";
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Error processing refund: " + ex.Message;
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    public async Task<IActionResult> Subscription(CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.CompanyId == companyId, ct);

        if (company == null) return NotFound();

        var sub = await _db.Subscriptions
            .Include(s => s.Plan)
            .OrderByDescending(s => s.SubscriptionId)
            .FirstOrDefaultAsync(s => s.CompanyId == companyId, ct);

        var model = new Models.Admin.SubscriptionViewModel
        {
            Company = company,
            Subscription = sub
        };

        ViewData["Title"] = "Subscription & Billing";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ManageSubscription(CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var company = await _db.Companies
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CompanyId == companyId, ct);

        if (company == null || string.IsNullOrWhiteSpace(company.StripeCustomerId))
        {
            TempData["ErrorMessage"] = "No billing record found. If you just signed up, please wait for payment confirmation.";
            return RedirectToAction(nameof(Subscription));
        }

        try
        {
            var returnUrl = Url.Action(nameof(Subscription), "Admin", null, Request.Scheme);
            var portalUrl = await _stripe.CreateCustomerPortalSessionAsync(company.StripeCustomerId, returnUrl!, ct);

            if (string.IsNullOrWhiteSpace(portalUrl))
            {
                TempData["ErrorMessage"] = "Could not create billing portal session.";
                return RedirectToAction(nameof(Subscription));
            }

            return Redirect(portalUrl);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = "Error accessing billing portal: " + ex.Message;
            return RedirectToAction(nameof(Subscription));
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Approve(int id, string? notes, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var wfInstance = await _db.WorkflowInstances
            .FirstOrDefaultAsync(w => w.ReferenceId == id && w.ReferenceType == "Reservation" && w.CompanyId == companyId, ct);

        if (wfInstance != null && wfInstance.Status == "Pending")
        {
            wfInstance.Status = "Approved";
            wfInstance.CurrentStep = 0;

            var step = new WorkflowInstanceStep
            {
                InstanceId = wfInstance.InstanceId,
                ActionTaken = "Approved",
                PerformedBy = userId,
                PerformedAt = ViaReservaERP.AppTime.Now
            };
            _db.WorkflowInstanceSteps.Add(step);

            var res = await _db.Reservations.Include(r => r.Guest).FirstOrDefaultAsync(r => r.ReservationId == id);
            if (res != null) 
            {
                res.Status = "Confirmed";
                
                if (res.Guest?.UserId != null)
                {
                    await _notify.NotifyUserAsync(res.Guest.UserId.Value,
                        "Reservation Confirmed",
                        $"Your reservation #{res.ReservationId} has been approved and confirmed.",
                        "Reservation",
                        companyId,
                        ct);
                }
            }

            await _db.SaveChangesAsync(ct);
            TempData["SuccessMessage"] = "Reservation approved and confirmed.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(int id, string? notes, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var wfInstance = await _db.WorkflowInstances
            .FirstOrDefaultAsync(w => w.ReferenceId == id && w.ReferenceType == "Reservation" && w.CompanyId == companyId, ct);

        if (wfInstance != null && wfInstance.Status == "Pending")
        {
            wfInstance.Status = "Rejected";
            
            var step = new WorkflowInstanceStep
            {
                InstanceId = wfInstance.InstanceId,
                ActionTaken = "Rejected",
                PerformedBy = userId,
                PerformedAt = ViaReservaERP.AppTime.Now
            };
            _db.WorkflowInstanceSteps.Add(step);

            var res = await _db.Reservations.Include(r => r.Guest).FirstOrDefaultAsync(r => r.ReservationId == id);
            if (res != null) 
            {
                res.Status = "Cancelled";

                if (res.Guest?.UserId != null)
                {
                    await _notify.NotifyUserAsync(res.Guest.UserId.Value,
                        "Reservation Rejected",
                        $"Your reservation #{res.ReservationId} could not be confirmed and has been cancelled.",
                        "Reservation",
                        companyId,
                        ct);
                }
            }

            await _db.SaveChangesAsync(ct);
            TempData["SuccessMessage"] = "Reservation rejected.";
        }

        return RedirectToAction(nameof(Details), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveWorkflowInstance(int instanceId, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var wfInstance = await _db.WorkflowInstances
            .FirstOrDefaultAsync(w => w.InstanceId == instanceId && w.CompanyId == companyId, ct);

        if (wfInstance != null && wfInstance.Status == "Pending")
        {
            wfInstance.Status = "Approved";
            wfInstance.CurrentStep = 0;

            var step = new WorkflowInstanceStep
            {
                InstanceId = wfInstance.InstanceId,
                ActionTaken = "Approved",
                PerformedBy = userId,
                PerformedAt = ViaReservaERP.AppTime.Now
            };
            _db.WorkflowInstanceSteps.Add(step);

            if (wfInstance.ReferenceType == "Reservation" && wfInstance.ReferenceId.HasValue)
            {
                var res = await _db.Reservations.Include(r => r.Guest).FirstOrDefaultAsync(r => r.ReservationId == wfInstance.ReferenceId.Value);
                if (res != null) 
                {
                    res.Status = "Confirmed";
                    if (res.Guest?.UserId != null)
                    {
                        await _notify.NotifyUserAsync(res.Guest.UserId.Value,
                            "Reservation Confirmed",
                            $"Your reservation #{res.ReservationId} has been approved and confirmed.",
                            "Reservation",
                            companyId,
                            ct);
                    }
                }
            }
            else if (wfInstance.ReferenceType == "Guest" && wfInstance.ReferenceId.HasValue)
            {
                var guest = await _db.Guests.FindAsync(wfInstance.ReferenceId.Value);
                if (guest != null) guest.IsActive = true;
            }

            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserId = userId,
                Action = "ApproveWorkflow",
                TableName = "WorkflowInstances",
                RecordId = wfInstance.InstanceId,
                NewValues = $"Approved workflow for {wfInstance.ReferenceType} #{wfInstance.ReferenceId}",
                ActionDate = ViaReservaERP.AppTime.Now
            });

            await _db.SaveChangesAsync(ct);
            TempData["SuccessMessage"] = "Workflow approved successfully.";
        }

        return RedirectToAction(nameof(Approvals));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RejectWorkflowInstance(int instanceId, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var wfInstance = await _db.WorkflowInstances
            .FirstOrDefaultAsync(w => w.InstanceId == instanceId && w.CompanyId == companyId, ct);

        if (wfInstance != null && wfInstance.Status == "Pending")
        {
            wfInstance.Status = "Rejected";

            var step = new WorkflowInstanceStep
            {
                InstanceId = wfInstance.InstanceId,
                ActionTaken = "Rejected",
                PerformedBy = userId,
                PerformedAt = ViaReservaERP.AppTime.Now
            };
            _db.WorkflowInstanceSteps.Add(step);

            if (wfInstance.ReferenceType == "Reservation" && wfInstance.ReferenceId.HasValue)
            {
                var res = await _db.Reservations.FindAsync(wfInstance.ReferenceId.Value);
                if (res != null) res.Status = "Cancelled";
            }

            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserId = userId,
                Action = "RejectWorkflow",
                TableName = "WorkflowInstances",
                RecordId = wfInstance.InstanceId,
                NewValues = $"Rejected workflow for {wfInstance.ReferenceType} #{wfInstance.ReferenceId}",
                ActionDate = ViaReservaERP.AppTime.Now
            });

            await _db.SaveChangesAsync(ct);
            TempData["SuccessMessage"] = "Workflow rejected.";
        }

        return RedirectToAction(nameof(Approvals));
    }

    public async Task<IActionResult> Approvals(int page = 1, int pageSize = 10, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var now = ViaReservaERP.AppTime.Now;
        var slaCutoff = now.AddMinutes(-120);

        var baseQuery = _db.WorkflowInstances
            .Include(wi => wi.Workflow)
            .Where(wi => wi.CompanyId == companyId && wi.Status == "Pending");

        var totalRows = await baseQuery.CountAsync(ct);
        var rows = await baseQuery
            .OrderByDescending(wi => wi.CreatedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        var escalated = await _db.ServiceRequests
            .Include(sr => sr.Service)
            .Include(sr => sr.Guest)
            .Where(sr => sr.CompanyId == companyId && sr.Status == "Escalated")
            .OrderByDescending(sr => sr.RequestDate)
            .ToListAsync(ct);

        var model = new Models.Admin.ApprovalCenterViewModel
        {
            Rows = rows,
            EscalatedRequests = escalated,
            TotalPending = totalRows,
            OverdueApprovals = await baseQuery.CountAsync(wi => wi.CreatedAt < slaCutoff, ct),
            HighValueRequests = await baseQuery.CountAsync(wi => wi.ReferenceType == "Payment" || wi.ReferenceType == "Refund", ct),
            EscalatedOperational = escalated.Count,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows
        };

        ViewData["Title"] = "Approval Center";
        return View(model);
    }

    public async Task<IActionResult> AuditLogs(string? search, string? action, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var query = _db.AuditLogs
            .Include(a => a.User)
            .Where(a => a.CompanyId == companyId);

        if (!string.IsNullOrEmpty(search))
        {
            query = query.Where(a => a.TableName.Contains(search) || a.NewValues.Contains(search));
        }

        if (!string.IsNullOrEmpty(action))
        {
            query = query.Where(a => a.Action == action);
        }

        var logs = await query.OrderByDescending(a => a.ActionDate).Take(100).ToListAsync(ct);

        ViewData["Title"] = "Company Audit Logs";
        return View(logs);
    }

    [HttpGet]
    public async Task<IActionResult> Payment(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var res = await _db.Reservations
            .Include(r => r.Guest)
            .FirstOrDefaultAsync(r => r.ReservationId == id && r.CompanyId == companyId, ct);

        if (res == null) return NotFound();

        var paid = await _db.Payments
            .Where(p => p.ReservationId == id && p.Status == "Succeeded")
            .SumAsync(p => p.Amount ?? 0m, ct);

        var model = new Models.Admin.PaymentViewModel
        {
            ReservationId = id,
            GuestName = res.Guest?.FullName ?? "Unknown",
            TotalAmount = res.TotalAmount ?? 0m,
            AlreadyPaid = paid,
            PaymentAmount = Math.Max(0, (res.TotalAmount ?? 0m) - paid)
        };

        return PartialView("_PaymentModal", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessPayment(Models.Admin.PaymentViewModel model, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        if (model.PaymentAmount <= 0 && model.PaymentType != "Refund")
        {
            ModelState.AddModelError("", "Payment amount must be greater than zero.");
        }

        if (ModelState.IsValid)
        {
            var finalAmount = model.PaymentType == "Refund" ? -Math.Abs(model.PaymentAmount) : model.PaymentAmount;

            var res = await _db.Reservations
                .AsNoTracking()
                .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.ReservationId == model.ReservationId, ct);

            var total = res?.TotalAmount ?? 0m;
            var ratio = total > 0m ? (Math.Abs(finalAmount) / total) : 0m;
            var sign = finalAmount < 0m ? -1m : 1m;

            var payment = new Payment
            {
                CompanyId = companyId,
                ReservationId = model.ReservationId,
                Amount = finalAmount,
                PaymentMethod = model.PaymentMethod,
                Status = "Succeeded",
                CreatedAt = ViaReservaERP.AppTime.Now
            };

            _db.Payments.Add(payment);

            var transaction = new AccountingTransaction
            {
                CompanyId = companyId,
                Subtotal = (res?.Subtotal ?? 0m) * ratio * sign,
                TaxAmount = (res?.TaxAmount ?? 0m) * ratio * sign,
                ServiceCharge = (res?.ServiceCharge ?? 0m) * ratio * sign,
                Amount = finalAmount,
                Type = model.PaymentType == "Refund" ? "Expense" : "Income",
                Description = $"{model.PaymentType} Payment for Res #{model.ReservationId} via {model.PaymentMethod}",
                ReferenceId = model.ReservationId,
                ReferenceType = "Reservation",
                TransactionDate = ViaReservaERP.AppTime.Now
            };
            _db.Transactions.Add(transaction);

            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserId = userId,
                Action = "Insert",
                TableName = "Payments",
                RecordId = model.ReservationId,
                NewValues = $"Payment of {finalAmount} processed for Res #{model.ReservationId}",
                ActionDate = ViaReservaERP.AppTime.Now
            });

            await _db.SaveChangesAsync(ct);
            TempData["SuccessMessage"] = "Payment processed successfully.";
            return RedirectToAction(nameof(Reservations));
        }

        return RedirectToAction(nameof(Reservations));
    }

    public async Task<IActionResult> Invoice(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var res = await _db.Reservations
            .Include(r => r.Guest)
            .Include(r => r.Company)
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room).ThenInclude(rm => rm.RoomType)
            .FirstOrDefaultAsync(r => r.ReservationId == id && r.CompanyId == companyId, ct);

        if (res == null) return NotFound();

        var payments = await _db.Payments
            .Where(p => p.ReservationId == id && p.Status == "Succeeded")
            .ToListAsync(ct);

        var model = new Models.Admin.InvoiceViewModel
        {
            Reservation = res,
            Payments = payments,
            TotalAmount = res.TotalAmount ?? 0m,
            TotalPaid = payments.Sum(p => p.Amount ?? 0m)
        };

        ViewData["Title"] = $"Invoice #{id}";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> CheckIn(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var res = await _db.Reservations
            .Include(r => r.Guest)
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room)
            .FirstOrDefaultAsync(r => r.ReservationId == id && r.CompanyId == companyId, ct);

        if (res == null) return NotFound();

        var paid = await _db.Payments
            .Where(p => p.ReservationId == id && p.Status == "Succeeded")
            .SumAsync(p => p.Amount.HasValue ? p.Amount.Value : 0m, ct);

        var room = res.ReservationRooms.FirstOrDefault();

        var model = new Models.Admin.CheckInViewModel
        {
            Reservation = res,
            TotalAmount = res.TotalAmount ?? 0m,
            TotalPaid = paid,
            IsRoomAssigned = room != null,
            AssignedRoomNumber = room?.Room?.RoomNumber
        };

        ViewData["Title"] = "Guest Check-In";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessCheckIn(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var res = await _db.Reservations
            .Include(r => r.ReservationRooms)
            .FirstOrDefaultAsync(r => r.ReservationId == id && r.CompanyId == companyId, ct);

        if (res == null) return NotFound();

        if (res.Status != "Confirmed")
        {
            TempData["ErrorMessage"] = "Only confirmed reservations can be checked in.";
            return RedirectToAction(nameof(CheckIn), new { id });
        }

        var roomAssignment = res.ReservationRooms.FirstOrDefault();
        if (roomAssignment == null)
        {
            TempData["ErrorMessage"] = "No room assigned to this reservation.";
            return RedirectToAction(nameof(CheckIn), new { id });
        }

        using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // Update Reservation
            res.Status = "Checked In";

            // Update Room
            var room = await _db.Rooms.FindAsync(roomAssignment.RoomId);
            if (room != null)
            {
                room.Status = "Occupied";
            }

            // Audit Log
            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserId = userId,
                Action = "Update",
                TableName = "Reservations",
                RecordId = id,
                NewValues = $"Guest checked in to Room {room?.RoomNumber}",
                ActionDate = ViaReservaERP.AppTime.Now
            });

            // Notify Guest
            var guest = await _db.Guests.FindAsync(res.GuestId);
            if (guest?.UserId != null)
            {
                await _notify.NotifyUserAsync(guest.UserId.Value,
                    "Check-In Confirmed",
                    $"You have been checked in to Room {room?.RoomNumber}. Enjoy your stay!",
                    "Reservation",
                    companyId,
                    ct);
            }

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            TempData["SuccessMessage"] = "Guest checked in successfully.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(ct);
            TempData["ErrorMessage"] = "An error occurred during check-in.";
            return RedirectToAction(nameof(CheckIn), new { id });
        }
    }

    [HttpGet]
    public async Task<IActionResult> CheckOut(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var res = await _db.Reservations
            .Include(r => r.Guest)
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room)
            .FirstOrDefaultAsync(r => r.ReservationId == id && r.CompanyId == companyId, ct);

        if (res == null) return NotFound();

        var paid = await _db.Payments
            .Where(p => p.ReservationId == id && p.Status == "Succeeded")
            .SumAsync(p => p.Amount.HasValue ? p.Amount.Value : 0m, ct);

        var model = new Models.Admin.CheckInViewModel
        {
            Reservation = res,
            TotalAmount = res.TotalAmount ?? 0m,
            TotalPaid = paid,
            IsRoomAssigned = res.ReservationRooms.Any(),
            AssignedRoomNumber = res.ReservationRooms.FirstOrDefault()?.Room?.RoomNumber
        };

        ViewData["Title"] = "Guest Check-Out (Settlement)";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessCheckOut(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var res = await _db.Reservations
            .Include(r => r.ReservationRooms)
            .FirstOrDefaultAsync(r => r.ReservationId == id && r.CompanyId == companyId, ct);

        if (res == null) return NotFound();

        var paid = await _db.Payments
            .Where(p => p.ReservationId == id && p.Status == "Succeeded")
            .SumAsync(p => p.Amount.HasValue ? p.Amount.Value : 0m, ct);

        if ((res.TotalAmount ?? 0m) - paid > 0)
        {
            TempData["ErrorMessage"] = "Reservation has an outstanding balance. Please settle payment before check-out.";
            return RedirectToAction(nameof(CheckOut), new { id });
        }

        using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            res.Status = "Completed";

            var roomAssignment = res.ReservationRooms.FirstOrDefault();
            if (roomAssignment != null)
            {
                var room = await _db.Rooms.FindAsync(roomAssignment.RoomId);
                if (room != null)
                {
                    room.Status = "Available"; // Automatically make room available after check-out
                }
            }

            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserId = userId,
                Action = "Update",
                TableName = "Reservations",
                RecordId = id,
                NewValues = "Guest checked out. Stay completed.",
                ActionDate = ViaReservaERP.AppTime.Now
            });

            // Notify Guest
            var guest = await _db.Guests.FindAsync(res.GuestId);
            if (guest?.UserId != null)
            {
                await _notify.NotifyUserAsync(guest.UserId.Value,
                    "Check-Out Confirmed",
                    "You have been successfully checked out. Thank you for staying with us!",
                    "Reservation",
                    companyId,
                    ct);
            }

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            TempData["SuccessMessage"] = "Guest checked out successfully. Room marked for cleaning.";
            return RedirectToAction(nameof(Details), new { id });
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(ct);
            TempData["ErrorMessage"] = "An error occurred during check-out.";
            return RedirectToAction(nameof(CheckOut), new { id });
        }
    }

    [HttpGet]
    public async Task<IActionResult> AddService(int id, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var res = await _db.Reservations
            .Include(r => r.Guest)
            .FirstOrDefaultAsync(r => r.ReservationId == id && r.CompanyId == companyId, ct);

        if (res == null) return NotFound();

        var services = await _db.Services
            .Where(s => s.CompanyId == companyId)
            .OrderBy(s => s.ServiceName)
            .Select(s => new SelectListItem 
            { 
                Value = s.ServiceId.ToString(), 
                Text = s.ServiceName + " - ₱ " + (s.Price.HasValue ? s.Price.Value.ToString() : "0.00")
            })
            .ToListAsync(ct);

        var model = new Models.Admin.AddServiceViewModel
        {
            ReservationId = id,
            GuestName = res.Guest?.FullName ?? "Unknown",
            AvailableServices = services
        };

        return PartialView("_AddServiceModal", model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ProcessAddService(Models.Admin.AddServiceViewModel model, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId();

        var res = await _db.Reservations.FirstOrDefaultAsync(r => r.ReservationId == model.ReservationId && r.CompanyId == companyId, ct);
        if (res == null) return NotFound();

        var service = await _db.Services.FirstOrDefaultAsync(s => s.ServiceId == model.SelectedServiceId && s.CompanyId == companyId, ct);
        if (service == null) return NotFound();

        using var transaction = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            // 1. Create Service Request (for staff to see in their queue)
            var request = new ServiceRequest
            {
                CompanyId = companyId,
                GuestId = res.GuestId,
                ServiceId = service.ServiceId,
                RequestDate = ViaReservaERP.AppTime.Now,
                Status = "Pending"
                // Notes removed as it's missing in schema
            };
            _db.ServiceRequests.Add(request);

            // 2. If it has a price, create a ReservationService charge
            if (service.Price > 0)
            {
                var charge = new ReservationService
                {
                    ReservationId = model.ReservationId,
                    ServiceId = service.ServiceId,
                    Price = service.Price,
                    Quantity = model.Quantity
                    // DateAdded removed as it's missing in schema
                };
                _db.ReservationServices.Add(charge);

                // Update Folio Total
                res.TotalAmount = (res.TotalAmount ?? 0m) + (service.Price * model.Quantity);
            }

            // Audit Log
            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserId = userId,
                Action = "Insert",
                TableName = "ServiceRequests",
                RecordId = model.ReservationId,
                NewValues = $"Service '{service.ServiceName}' added for Res #{model.ReservationId}. Folio updated.",
                ActionDate = ViaReservaERP.AppTime.Now
            });

            await _db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);

            TempData["SuccessMessage"] = "Service request added successfully.";
        }
        catch (Exception)
        {
            await transaction.RollbackAsync(ct);
            TempData["ErrorMessage"] = "An error occurred while adding the service.";
        }

        return RedirectToAction(nameof(Details), new { id = model.ReservationId });
    }

    public async Task<IActionResult> Profile(CancellationToken ct)
    {
        var userId = CurrentUserId;
        var user = await _db.Users
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.UserId == userId, ct);

        ViewData["Title"] = "Profile Settings";
        ViewData["UserEmail"] = user?.Email;
        ViewData["AvatarUrl"] = user?.AvatarUrl;

        return View(user);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateAvatar(IFormFile avatar, CancellationToken ct = default)
    {
        var userId = CurrentUserId;
        if (userId <= 0) return Forbid();

        if (avatar == null || avatar.Length == 0)
        {
            TempData["ErrorMessage"] = "Please select a valid image file.";
            return RedirectToAction(nameof(Profile));
        }

        // Validate extension
        var ext = Path.GetExtension(avatar.FileName).ToLower();
        var allowed = new[] { ".jpg", ".jpeg", ".png", ".webp" };
        if (!allowed.Contains(ext))
        {
            TempData["ErrorMessage"] = "Only JPG, PNG and WEBP images are allowed.";
            return RedirectToAction(nameof(Profile));
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct);
        if (user == null) return NotFound();

        // Create directory
        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "avatars");
        if (!Directory.Exists(uploadsDir)) Directory.CreateDirectory(uploadsDir);

        // Generate unique name
        var fileName = $"avatar_{userId}_{DateTime.Now.Ticks}{ext}";
        var filePath = Path.Combine(uploadsDir, fileName);

        // Save file
        using (var stream = new FileStream(filePath, FileMode.Create))
        {
            await avatar.CopyToAsync(stream, ct);
        }

        // Delete old avatar if exists
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
    public async Task<IActionResult> UpdateProfile(string fullName, string? newPassword, CancellationToken ct)
    {
        var companyId = CurrentCompanyId;
        var userId = CurrentUserId;

        if (string.IsNullOrWhiteSpace(fullName))
        {
            TempData["ErrorMessage"] = "Full name is required.";
            return RedirectToAction(nameof(Profile));
        }

        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId && u.CompanyId == companyId, ct);
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
            CompanyId = companyId,
            UserId = userId,
            Action = "Update",
            TableName = "Users",
            RecordId = userId,
            NewValues = "Profile updated",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Profile updated.";
        return RedirectToAction(nameof(Profile));
    }

    [HttpGet]
    [Authorize(Policy = RoleNames.CompanyAdmin)]
    public async Task<IActionResult> AuditLogs(string? search, DateOnly? startDate = null, DateOnly? endDate = null, int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        if (page < 1) page = 1;
        var companyId = CurrentCompanyId;

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        var start = startDate ?? new DateOnly(today.Year, today.Month, 1);
        var end = endDate ?? new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));
        if (end < start) (start, end) = (end, start);

        static DateTime AsUtcStart(DateOnly d) => DateTime.SpecifyKind(d.ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        static DateTime AsUtcEndExclusive(DateOnly d) => DateTime.SpecifyKind(d.AddDays(1).ToDateTime(TimeOnly.MinValue), DateTimeKind.Utc);
        var startUtc = AsUtcStart(start);
        var endUtcExcl = AsUtcEndExclusive(end);

        var baseQ = _db.AuditLogs
            .AsNoTracking()
            .Include(a => a.User)
            .ThenInclude(u => u!.Role)
            .Where(a => a.CompanyId == companyId)
            .Where(a => a.ActionDate >= startUtc && a.ActionDate < endUtcExcl);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            baseQ = baseQ.Where(a => (a.User != null && a.User.FullName.Contains(term)) || (a.Action != null && a.Action.Contains(term)) || (a.TableName != null && a.TableName.Contains(term)));
        }

        var totalRows = await baseQ.CountAsync(ct);
        var rows = await baseQ
            .OrderByDescending(a => a.ActionDate)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AdminAuditLogRow
            {
                AuditId = a.AuditId,
                UserName = a.User != null ? a.User.FullName : "System",
                UserRole = a.User != null && a.User.Role != null ? a.User.Role.RoleName : "Service",
                Module = NormalizeModule(a.TableName ?? ""),
                Action = a.Action ?? "Action",
                IpAddress = a.IPAddress ?? "0.0.0.0",
                Status = (a.Action ?? "").ToLower().Contains("fail") || (a.Action ?? "").ToLower().Contains("error") ? "Failed" : "OK",
                Timestamp = a.ActionDate,
                OldValues = a.OldValues,
                NewValues = a.NewValues
            })
            .ToListAsync(ct);

        var todayStartUtc = AsUtcStart(today);
        var todayEndUtcExcl = AsUtcEndExclusive(today);
        var totalToday = await _db.AuditLogs
            .AsNoTracking()
            .Where(a => a.CompanyId == companyId && a.ActionDate >= todayStartUtc && a.ActionDate < todayEndUtcExcl)
            .CountAsync(ct);

        var securityAlerts = await baseQ.CountAsync(a => (a.Action ?? "").ToLower().Contains("unauthorized") || (a.Action ?? "").ToLower().Contains("lockout"), ct);
        var loginAttempts = await baseQ.CountAsync(a => (a.Action ?? "").ToLower().Contains("login"), ct);
        var critical = await baseQ.CountAsync(a => (a.Action ?? "").ToLower().Contains("delete") || (a.Action ?? "").ToLower().Contains("role") || (a.Action ?? "").ToLower().Contains("critical"), ct);

        var model = new AdminAuditLogsViewModel
        {
            Search = search,
            StartDate = start,
            EndDate = end,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            TotalLogsToday = totalToday,
            SecurityAlerts = securityAlerts,
            LoginAttempts = loginAttempts,
            CriticalEvents = critical,
            Rows = rows
        };

        return View(model);
    }

    private static string NormalizeModule(string tableName)
    {
        var t = (tableName ?? "").Trim().ToLowerInvariant();
        if (t.Contains("reservation")) return "Reservations";
        if (t.Contains("service")) return "Guest Services";
        if (t.Contains("workflow")) return "Workflow Management";
        if (t.Contains("payment") || t.Contains("transaction") || t.Contains("account")) return "Accounting";
        if (t.Contains("user") || t.Contains("role") || t.Contains("permission")) return "User Management";
        if (t.Contains("room") || t.Contains("guest")) return "Property Management";
        return "System";
    }

    public async Task<IActionResult> Notifications(CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var userId = CurrentUserId;

        var items = await _db.Notifications
            .Where(n => n.CompanyId == companyId && n.UserId == userId)
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
        var companyId = CurrentCompanyId;
        var userId = CurrentUserId;

        var unread = await _db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.CompanyId == companyId && n.UserId == userId && !n.IsRead, ct);

        return Json(new { unread });
    }

    [HttpGet]
    public async Task<IActionResult> NotificationsListPartial(CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var userId = CurrentUserId;

        var items = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.CompanyId == companyId && n.UserId == userId)
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
        var companyId = CurrentCompanyId;
        var userId = CurrentUserId;

        var unread = await _db.Notifications
            .Where(n => n.CompanyId == companyId && n.UserId == userId && !n.IsRead)
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