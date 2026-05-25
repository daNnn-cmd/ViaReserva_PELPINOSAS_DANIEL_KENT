using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ViaReservaERP.Data;
using ViaReservaERP.Models;
using ViaReservaERP.Models.GuestPortal;
using ViaReservaERP.Services;
using ViaReservaERP.Security;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using ClosedXML.Excel;
using Stripe;
using System.Globalization;

namespace ViaReservaERP.Controllers;

[Authorize(Policy = RoleNames.Guest)]
public class GuestController : Controller
{
    private readonly ViaReservaDbContext _db;
    private readonly INotificationService _notify;
    private readonly IWebHostEnvironment _env;
    private readonly IConfiguration _config;
    private readonly ITaxService _tax;

    public GuestController(ViaReservaDbContext db, INotificationService notify, IWebHostEnvironment env, IConfiguration config, ITaxService tax)
    {
        _db = db;
        _notify = notify;
        _env = env;
        _config = config;
        _tax = tax;
    }

    private async Task<Guest> RequireCurrentGuestAsync(CancellationToken ct)
    {
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (!userId.HasValue)
            throw new InvalidOperationException("Missing authentication context.");

        Guest? guest;

        if (companyId.HasValue)
        {
            guest = await _db.Guests
                .AsNoTracking()
                .Include(g => g.Company)
                .FirstOrDefaultAsync(g => g.UserId == userId.Value && g.CompanyId == companyId.Value, ct);
        }
        else
        {
            guest = null;
        }

        guest ??= await _db.Guests
            .AsNoTracking()
            .Include(g => g.Company)
            .Where(g => g.UserId == userId.Value)
            .OrderByDescending(g => g.GuestId)
            .FirstOrDefaultAsync(ct);

        if (guest is null)
            throw new InvalidOperationException("Guest profile not found.");

        return guest;
    }

    [HttpGet]
    public async Task<IActionResult> Dashboard(int? reservationId = null, CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);

        var reservations = await _db.Reservations
            .AsNoTracking()
            .Where(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId)
            .OrderByDescending(r => r.ReservationId)
            .Take(40)
            .Select(r => new ReservationRow
            {
                ReservationId = r.ReservationId,
                Status = r.Status ?? "",
                CheckInDate = r.CheckInDate,
                CheckOutDate = r.CheckOutDate,
                RoomNumber = r.ReservationRooms.Select(rr => rr.Room!.RoomNumber).FirstOrDefault() ?? "",
                TotalAmount = r.TotalAmount ?? 0m
            })
            .ToListAsync(ct);

        bool IsCancelled(ReservationRow r) => (r.Status ?? "").Contains("Cancel", StringComparison.OrdinalIgnoreCase);
        bool IsCompleted(ReservationRow r) => (r.Status ?? "").Contains("Complete", StringComparison.OrdinalIgnoreCase) || (r.Status ?? "").Contains("Out", StringComparison.OrdinalIgnoreCase) || (r.CheckOutDate != null && r.CheckOutDate.Value < today && !IsCancelled(r));
        bool IsActive(ReservationRow r) => (r.Status ?? "").Contains("In", StringComparison.OrdinalIgnoreCase) && !(r.Status ?? "").Contains("Out", StringComparison.OrdinalIgnoreCase) && (r.CheckOutDate == null || r.CheckOutDate.Value >= today);
        bool IsUpcoming(ReservationRow r) => !IsCancelled(r) && !IsCompleted(r) && !IsActive(r);

        var upcoming = reservations.Count(IsUpcoming);
        var active = reservations.Count(IsActive);
        var completed = reservations.Count(IsCompleted);

        var pendingServices = await _db.ServiceRequests
            .AsNoTracking()
            .CountAsync(sr => sr.CompanyId == guest.CompanyId && sr.GuestId == guest.GuestId && (sr.Status ?? "").ToLower().Contains("pend"), ct);

        var paymentRows = await _db.Payments
            .AsNoTracking()
            .Where(p => p.CompanyId == guest.CompanyId)
            .Where(p => p.ReservationId != null)
            .Join(
                _db.Reservations.AsNoTracking().Where(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId),
                p => p.ReservationId,
                r => (int?)r.ReservationId,
                (p, r) => new PaymentRow
                {
                    PaymentId = p.PaymentId,
                    ReservationId = p.ReservationId,
                    Amount = p.Amount ?? 0m,
                    Status = p.Status ?? "",
                    PaymentMethod = p.PaymentMethod ?? "",
                    CreatedAtUtc = p.CreatedAt,
                    StripePaymentIntentId = p.StripePaymentIntentId ?? ""
                })
            .OrderByDescending(p => p.PaymentId)
            .Take(20)
            .ToListAsync(ct);

        var totalPaid = paymentRows
            .Where(p => (p.Status ?? "").ToLower().Contains("succeed"))
            .Sum(p => p.Amount);

        ReservationSummaryCard? highlight = null;
        if (reservationId.HasValue)
        {
            var h = await _db.Reservations
                .AsNoTracking()
                .Include(r => r.Payments)
                .FirstOrDefaultAsync(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId && r.ReservationId == reservationId.Value, ct);

            if (h != null)
            {
                highlight = new ReservationSummaryCard
                {
                    ReservationId = h.ReservationId,
                    Status = h.Status ?? "",
                    CheckInDate = h.CheckInDate,
                    CheckOutDate = h.CheckOutDate,
                    TotalAmount = h.TotalAmount ?? 0m,
                    PaymentStatus = h.Payments.OrderByDescending(p => p.PaymentId).Select(p => p.Status).FirstOrDefault() ?? ""
                };
            }
        }

        var recentRequests = await _db.ServiceRequests
            .AsNoTracking()
            .Include(sr => sr.Service)
            .Where(sr => sr.CompanyId == guest.CompanyId && sr.GuestId == guest.GuestId)
            .OrderByDescending(sr => sr.RequestDate)
            .Take(15)
            .Select(sr => new ServiceRequestRow
            {
                RequestId = sr.RequestId,
                ReservationId = sr.ReservationId,
                ServiceName = sr.Service != null ? (sr.Service.ServiceName ?? "") : "",
                Price = sr.Service != null ? (sr.Service.Price ?? 0m) : 0m,
                Status = sr.Status ?? "",
                RequestedAtUtc = sr.RequestDate
            })
            .ToListAsync(ct);

        var model = new GuestDashboardViewModel
        {
            GuestId = guest.GuestId,
            GuestName = guest.FullName ?? "",
            CompanyName = guest.Company != null ? guest.Company.CompanyName : "",
            UpcomingBookings = upcoming,
            ActiveStays = active,
            CompletedStays = completed,
            PendingServices = pendingServices,
            TotalPaid = totalPaid,
            HighlightReservationId = reservationId,
            HighlightReservation = highlight,
            RecentReservations = reservations.Take(8).ToList(),
            RecentPayments = paymentRows.Take(8).ToList(),
            RecentServiceRequests = recentRequests.Take(8).ToList()
        };

        ViewData["Title"] = "Guest Dashboard";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Reservations(CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);

        var rows = await _db.Reservations
            .AsNoTracking()
            .Where(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId)
            .OrderByDescending(r => r.ReservationId)
            .Select(r => new ReservationRow
            {
                ReservationId = r.ReservationId,
                Status = r.Status ?? "",
                CheckInDate = r.CheckInDate,
                CheckOutDate = r.CheckOutDate,
                RoomNumber = r.ReservationRooms.Select(rr => rr.Room!.RoomNumber).FirstOrDefault() ?? "",
                TotalAmount = r.TotalAmount ?? 0m,
                Subtotal = r.Subtotal ?? 0m,
                TaxAmount = r.TaxAmount ?? 0m,
                ServiceCharge = r.ServiceCharge ?? 0m
            })
            .ToListAsync(ct);

        bool IsCancelled(ReservationRow r) => (r.Status ?? "").Contains("Cancel", StringComparison.OrdinalIgnoreCase);
        bool IsCompleted(ReservationRow r) => (r.Status ?? "").Contains("Complete", StringComparison.OrdinalIgnoreCase) || (r.Status ?? "").Contains("Out", StringComparison.OrdinalIgnoreCase) || (r.CheckOutDate != null && r.CheckOutDate.Value < today && !IsCancelled(r));
        bool IsActive(ReservationRow r) => (r.Status ?? "").Contains("In", StringComparison.OrdinalIgnoreCase) && !(r.Status ?? "").Contains("Out", StringComparison.OrdinalIgnoreCase) && (r.CheckOutDate == null || r.CheckOutDate.Value >= today);
        bool IsUpcoming(ReservationRow r) => !IsCancelled(r) && !IsCompleted(r) && !IsActive(r);

        var model = new GuestReservationsViewModel
        {
            GuestId = guest.GuestId,
            GuestName = guest.FullName ?? "",
            Upcoming = rows.Where(IsUpcoming).ToList(),
            Active = rows.Where(IsActive).ToList(),
            Completed = rows.Where(IsCompleted).ToList(),
            Cancelled = rows.Where(IsCancelled).ToList()
        };

        ViewData["Title"] = "My Reservations";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> ReservationDetails(int id, CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);

        var reservation = await _db.Reservations
            .AsNoTracking()
            .Where(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId && r.ReservationId == id)
            .Include(r => r.ReservationRooms)
                .ThenInclude(rr => rr.Room)
                    .ThenInclude(room => room!.RoomType)
            .Include(r => r.ReservationServices)
                .ThenInclude(rs => rs.Service)
            .Include(r => r.Payments)
            .FirstOrDefaultAsync(ct);

        if (reservation is null)
            return NotFound();

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        var statusLower = (reservation.Status ?? "").ToLowerInvariant();
        var isPendingOrConfirmed = statusLower.Contains("pend") || statusLower.Contains("confirm");
        var isCheckedIn = statusLower.Contains("checked in") || (statusLower.Contains("check") && statusLower.Contains("in") && !statusLower.Contains("out"));
        var canCancel = isPendingOrConfirmed && (reservation.CheckInDate == null || reservation.CheckInDate.Value.DayNumber - today.DayNumber >= 1);
        var canExtend = isCheckedIn && reservation.CheckOutDate != null;
        var canEarlyCheckOut = isCheckedIn && reservation.CheckOutDate != null && reservation.CheckOutDate.Value.DayNumber - today.DayNumber > 1;

        var model = new GuestReservationDetailsViewModel
        {
            ReservationId = reservation.ReservationId,
            CompanyName = guest.Company?.CompanyName ?? string.Empty,
            Status = reservation.Status ?? string.Empty,
            CheckInDate = reservation.CheckInDate,
            CheckOutDate = reservation.CheckOutDate,
            TotalAmount = reservation.TotalAmount ?? 0m,
            Subtotal = reservation.Subtotal ?? 0m,
            TaxAmount = reservation.TaxAmount ?? 0m,
            ServiceCharge = reservation.ServiceCharge ?? 0m,
            CanCancel = canCancel,
            CanExtendStay = canExtend,
            CanEarlyCheckOut = canEarlyCheckOut,
            Rooms = reservation.ReservationRooms
                .OrderBy(rr => rr.ReservationRoomId)
                .Select(rr => new GuestReservationRoomRow
                {
                    RoomNumber = rr.Room?.RoomNumber ?? string.Empty,
                    RoomType = rr.Room?.RoomType?.TypeName ?? string.Empty,
                    Price = rr.Price ?? 0m
                })
                .ToList(),
            Services = reservation.ReservationServices
                .OrderBy(rs => rs.ReservationServiceId)
                .Select(rs => new GuestReservationServiceRow
                {
                    ServiceName = rs.Service?.ServiceName ?? string.Empty,
                    Quantity = rs.Quantity ?? 0,
                    Price = rs.Price ?? 0m
                })
                .ToList(),
            Payments = reservation.Payments
                .OrderByDescending(p => p.PaymentId)
                .Select(p => new PaymentRow
                {
                    PaymentId = p.PaymentId,
                    ReservationId = p.ReservationId,
                    Amount = p.Amount ?? 0m,
                    Status = p.Status ?? string.Empty,
                    PaymentMethod = p.PaymentMethod ?? string.Empty,
                    CreatedAtUtc = p.CreatedAt,
                    StripePaymentIntentId = p.StripePaymentIntentId ?? string.Empty
                })
                .ToList()
        };

        ViewData["Title"] = $"Reservation #{reservation.ReservationId}";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CancelReservation(int id, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var guest = await RequireCurrentGuestAsync(ct);
        var reservation = await _db.Reservations
            .Include(r => r.Payments)
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room)
            .FirstOrDefaultAsync(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId && r.ReservationId == id, ct);

        if (reservation is null)
            return NotFound();

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        var status = (reservation.Status ?? "").ToLower();

        if (status.Contains("check") || status.Contains("active"))
            return BadRequest("Cannot cancel an active stay.");

        if (reservation.CheckInDate != null)
        {
            var daysToCheckIn = reservation.CheckInDate.Value.DayNumber - today.DayNumber;
            if (daysToCheckIn < 1)
                return BadRequest("Cancellation window has passed.");
        }

        reservation.Status = "Cancelled";

        // Release rooms back to Available
        foreach (var rr in reservation.ReservationRooms)
        {
            if (rr.Room != null)
                rr.Room.Status = "Available";
        }

        var wf = await _db.WorkflowInstances
            .FirstOrDefaultAsync(w => w.CompanyId == guest.CompanyId && w.ReferenceType == "Reservation" && w.ReferenceId == reservation.ReservationId, ct);
        if (wf != null)
            wf.Status = "Cancelled";

        var payment = reservation.Payments.OrderByDescending(p => p.PaymentId).FirstOrDefault();
        if (payment != null && (payment.Status ?? "").ToLower().Contains("succeed"))
        {
            payment.Status = "Refund Pending";
            var total = reservation.TotalAmount ?? 0m;
            var refundAmount = payment.Amount ?? 0m;
            var ratio = (total > 0m && refundAmount > 0m) ? (refundAmount / total) : 0m;

            _db.Transactions.Add(new AccountingTransaction
            {
                CompanyId = guest.CompanyId,
                Subtotal = (reservation.Subtotal ?? 0m) * -ratio,
                TaxAmount = (reservation.TaxAmount ?? 0m) * -ratio,
                ServiceCharge = (reservation.ServiceCharge ?? 0m) * -ratio,
                Amount = -(payment.Amount ?? 0m),
                Type = "Refund",
                Description = $"Reservation Cancellation Refund #{reservation.ReservationId}",
                ReferenceId = reservation.ReservationId,
                ReferenceType = "Reservation",
                TransactionDate = ViaReservaERP.AppTime.Now
            });
        }

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = guest.CompanyId,
            UserId = userId,
            Action = "Reservation Cancelled",
            TableName = "Reservations",
            RecordId = reservation.ReservationId,
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);

        // Notify guest
        if (userId.HasValue)
        {
            await _notify.NotifyUserAsync(userId.Value,
                "Reservation Cancelled",
                $"Your reservation #{reservation.ReservationId} has been cancelled. Refund status: {(payment?.Status ?? "N/A")}",
                "Reservation",
                guest.CompanyId,
                ct);
        }

        if (!string.IsNullOrWhiteSpace(guest.Email))
        {
            await _notify.EmailUserAsync(
                guest.Email,
                $"ViaReserva: Reservation Cancelled #{reservation.ReservationId}",
                $"Your reservation #{reservation.ReservationId} has been cancelled. Refund status: {(payment?.Status ?? "N/A")}.",
                html: null,
                ct);
        }

        // Notify Admin (2), Accounting (3), FrontDesk (4)
        var cancelMsg = $"Guest {guest.FullName} cancelled reservation #{reservation.ReservationId}. Refund status: {(payment?.Status ?? "N/A")}.";
        await _notify.NotifyRoleAsync(guest.CompanyId, roleId: 2, title: "Reservation Cancelled", message: cancelMsg, type: "Reservation", ct);
        await _notify.NotifyRoleAsync(guest.CompanyId, roleId: 3, title: "Cancellation Refund", message: cancelMsg, type: "Reservation", ct);
        await _notify.NotifyRoleAsync(guest.CompanyId, roleId: 4, title: "Reservation Cancelled", message: cancelMsg, type: "Reservation", ct);

        return RedirectToAction(nameof(Reservations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExtendStay(int id, DateOnly newCheckOutDate, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var guest = await RequireCurrentGuestAsync(ct);
        var reservation = await _db.Reservations
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room).ThenInclude(room => room!.RoomType)
            .FirstOrDefaultAsync(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId && r.ReservationId == id, ct);

        if (reservation is null) return NotFound();

        var status = (reservation.Status ?? "").ToLowerInvariant();
        if (!status.Contains("checked in") && !(status.Contains("check") && status.Contains("in") && !status.Contains("out")))
            return BadRequest("Can only extend a checked-in reservation.");

        if (reservation.CheckOutDate == null || newCheckOutDate <= reservation.CheckOutDate.Value)
            return BadRequest("New check-out date must be after the current check-out date.");

        var oldCheckOut = reservation.CheckOutDate.Value;
        var additionalNights = newCheckOutDate.DayNumber - oldCheckOut.DayNumber;
        if (additionalNights <= 0) return BadRequest("Invalid extension date.");

        var roomPrice = reservation.ReservationRooms.FirstOrDefault()?.Room?.RoomType?.BasePrice ?? 0m;
        var additionalBase = roomPrice * additionalNights;
        var taxResult = _tax.CalculateTaxes(additionalBase, guest.CompanyId);

        reservation.CheckOutDate = newCheckOutDate;
        reservation.Subtotal = (reservation.Subtotal ?? 0m) + taxResult.Subtotal;
        reservation.TaxAmount = (reservation.TaxAmount ?? 0m) + taxResult.TaxAmount;
        reservation.ServiceCharge = (reservation.ServiceCharge ?? 0m) + taxResult.ServiceCharge;
        reservation.TotalAmount = (reservation.TotalAmount ?? 0m) + taxResult.Total;

        _db.Transactions.Add(new AccountingTransaction
        {
            CompanyId = guest.CompanyId,
            Subtotal = taxResult.Subtotal,
            TaxAmount = taxResult.TaxAmount,
            ServiceCharge = taxResult.ServiceCharge,
            Amount = taxResult.Total,
            Type = "Income",
            Description = $"Stay Extension (+{additionalNights} nights) Res #{reservation.ReservationId}",
            ReferenceId = reservation.ReservationId,
            ReferenceType = "Reservation",
            TransactionDate = ViaReservaERP.AppTime.Now
        });

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = guest.CompanyId,
            UserId = userId,
            Action = "Stay Extended",
            TableName = "Reservations",
            RecordId = reservation.ReservationId,
            NewValues = $"Extended checkout from {oldCheckOut:yyyy-MM-dd} to {newCheckOutDate:yyyy-MM-dd} (+{additionalNights} nights, +₱{taxResult.Total:N2})",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);

        var extMsg = $"Guest {guest.FullName} extended stay for reservation #{reservation.ReservationId}. New checkout: {newCheckOutDate:MMM dd, yyyy} (+{additionalNights} nights, +₱{taxResult.Total:N2}).";
        if (userId.HasValue)
            await _notify.NotifyUserAsync(userId.Value, "Stay Extended", extMsg, "Reservation", guest.CompanyId, ct);
        await _notify.NotifyRoleAsync(guest.CompanyId, roleId: 2, title: "Stay Extended", message: extMsg, type: "Reservation", ct);
        await _notify.NotifyRoleAsync(guest.CompanyId, roleId: 3, title: "Stay Extension Billing", message: extMsg, type: "Reservation", ct);
        await _notify.NotifyRoleAsync(guest.CompanyId, roleId: 4, title: "Stay Extended", message: extMsg, type: "Reservation", ct);

        TempData["SuccessMessage"] = $"Stay extended to {newCheckOutDate:MMM dd, yyyy}. Additional charge: ₱{taxResult.Total:N2}.";
        return RedirectToAction(nameof(ReservationDetails), new { id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EarlyCheckOut(int id, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var guest = await RequireCurrentGuestAsync(ct);
        var reservation = await _db.Reservations
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room).ThenInclude(room => room!.RoomType)
            .Include(r => r.Payments)
            .FirstOrDefaultAsync(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId && r.ReservationId == id, ct);

        if (reservation is null) return NotFound();

        var status = (reservation.Status ?? "").ToLowerInvariant();
        if (!status.Contains("checked in") && !(status.Contains("check") && status.Contains("in") && !status.Contains("out")))
            return BadRequest("Can only early check-out a checked-in reservation.");

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        var originalCheckOut = reservation.CheckOutDate;

        if (originalCheckOut == null || originalCheckOut.Value.DayNumber - today.DayNumber <= 0)
            return BadRequest("No days to reduce for early check-out.");

        var reducedNights = originalCheckOut.Value.DayNumber - today.DayNumber;
        var roomPrice = reservation.ReservationRooms.FirstOrDefault()?.Room?.RoomType?.BasePrice ?? 0m;
        var refundBase = roomPrice * reducedNights;
        var refundTax = _tax.CalculateTaxes(refundBase, guest.CompanyId);

        reservation.CheckOutDate = today;
        reservation.Status = "Checked Out";
        reservation.Subtotal = Math.Max(0m, (reservation.Subtotal ?? 0m) - refundTax.Subtotal);
        reservation.TaxAmount = Math.Max(0m, (reservation.TaxAmount ?? 0m) - refundTax.TaxAmount);
        reservation.ServiceCharge = Math.Max(0m, (reservation.ServiceCharge ?? 0m) - refundTax.ServiceCharge);
        reservation.TotalAmount = Math.Max(0m, (reservation.TotalAmount ?? 0m) - refundTax.Total);

        // Release rooms
        foreach (var rr in reservation.ReservationRooms)
        {
            if (rr.Room != null)
                rr.Room.Status = "Available";
        }

        // Create refund transaction in accounting
        if (refundTax.Total > 0m)
        {
            _db.Transactions.Add(new AccountingTransaction
            {
                CompanyId = guest.CompanyId,
                Subtotal = -refundTax.Subtotal,
                TaxAmount = -refundTax.TaxAmount,
                ServiceCharge = -refundTax.ServiceCharge,
                Amount = -refundTax.Total,
                Type = "Refund",
                Description = $"Early Check-out Refund ({reducedNights} unused nights) Res #{reservation.ReservationId}",
                ReferenceId = reservation.ReservationId,
                ReferenceType = "Reservation",
                TransactionDate = ViaReservaERP.AppTime.Now
            });

            // Mark last succeeded payment for partial refund
            var lastPayment = reservation.Payments.OrderByDescending(p => p.PaymentId)
                .FirstOrDefault(p => (p.Status ?? "").ToLower().Contains("succeed"));
            if (lastPayment != null)
                lastPayment.Status = "Partial Refund Pending";
        }

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = guest.CompanyId,
            UserId = userId,
            Action = "Early Check-out",
            TableName = "Reservations",
            RecordId = reservation.ReservationId,
            NewValues = $"Early checkout on {today:yyyy-MM-dd} (original: {originalCheckOut:yyyy-MM-dd}). {reducedNights} unused nights refunded ₱{refundTax.Total:N2}",
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);

        var earlyMsg = $"Guest {guest.FullName} early checked-out reservation #{reservation.ReservationId}. {reducedNights} unused nights. Refund: ₱{refundTax.Total:N2}.";
        if (userId.HasValue)
            await _notify.NotifyUserAsync(userId.Value, "Early Check-out Completed", earlyMsg, "Reservation", guest.CompanyId, ct);
        await _notify.NotifyRoleAsync(guest.CompanyId, roleId: 2, title: "Early Check-out", message: earlyMsg, type: "Reservation", ct);
        await _notify.NotifyRoleAsync(guest.CompanyId, roleId: 3, title: "Early Check-out Refund", message: earlyMsg, type: "Reservation", ct);
        await _notify.NotifyRoleAsync(guest.CompanyId, roleId: 4, title: "Early Check-out", message: earlyMsg, type: "Reservation", ct);

        TempData["SuccessMessage"] = $"Checked out early. {reducedNights} unused night(s) refund of ₱{refundTax.Total:N2} has been initiated.";
        return RedirectToAction(nameof(Reservations));
    }

    [HttpGet]
    public async Task<IActionResult> Payments(CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);
        var rows = await GetPaymentRowsAsync(guest, ct);

        var balanceRows = await _db.Reservations
            .AsNoTracking()
            .Where(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId)
            .Select(r => new
            {
                r.ReservationId,
                Status = r.Status ?? string.Empty,
                Total = r.TotalAmount ?? 0m
            })
            .ToListAsync(ct);

        var reservationIds = balanceRows.Select(r => r.ReservationId).ToList();

        var paidMap = await _db.Payments
            .AsNoTracking()
            .Where(p => p.CompanyId == guest.CompanyId)
            .Where(p => p.ReservationId != null && reservationIds.Contains(p.ReservationId.Value))
            .Where(p => (p.Status ?? "").ToLower().Contains("succeed"))
            .GroupBy(p => p.ReservationId!.Value)
            .Select(g => new { ReservationId = g.Key, Paid = g.Sum(x => x.Amount ?? 0m) })
            .ToDictionaryAsync(x => x.ReservationId, x => x.Paid, ct);

        var balances = balanceRows
            .Select(r => new GuestBalanceRow
            {
                ReservationId = r.ReservationId,
                ReservationStatus = r.Status,
                TotalAmount = r.Total,
                PaidAmount = paidMap.TryGetValue(r.ReservationId, out var paid) ? paid : 0m,
                BalanceAmount = Math.Max(0m, r.Total - (paidMap.TryGetValue(r.ReservationId, out var p) ? p : 0m))
            })
            .Where(r => r.BalanceAmount > 0m)
            .OrderByDescending(r => r.ReservationId)
            .ToList();

        var model = new GuestPaymentsViewModel
        {
            GuestId = guest.GuestId,
            GuestName = guest.FullName ?? "",
            GuestEmail = guest.Email ?? "",
            Rows = rows,
            Balances = balances
        };

        ViewData["Title"] = "My Payments";
        ViewData["StripePublishableKey"] = _config["Stripe:PublishableKey"] ?? string.Empty;
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> TaxQuote(decimal baseAmount, CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);
        if (baseAmount < 0) return BadRequest(new { message = "Invalid amount." });

        var taxes = _tax.CalculateTaxes(baseAmount, guest.CompanyId);
        return Json(new
        {
            subtotal = taxes.Subtotal,
            serviceCharge = taxes.ServiceCharge,
            taxAmount = taxes.TaxAmount,
            total = taxes.Total
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBalancePaymentIntent(int reservationId, CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);

        var reservation = await _db.Reservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId && r.ReservationId == reservationId, ct);

        if (reservation == null)
            return NotFound(new { message = "Reservation not found." });

        var total = reservation.TotalAmount ?? 0m;
        var paid = await _db.Payments
            .AsNoTracking()
            .Where(p => p.CompanyId == guest.CompanyId && p.ReservationId == reservationId)
            .Where(p => (p.Status ?? "").ToLower().Contains("succeed"))
            .SumAsync(p => p.Amount ?? 0m, ct);

        var balance = total - paid;
        if (balance <= 0m)
            return BadRequest(new { message = "No outstanding balance for this reservation." });

        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
            return BadRequest(new { message = "Stripe is not configured." });

        var amount = (long)Math.Round(balance * 100m, MidpointRounding.AwayFromZero);
        if (amount <= 0)
            return BadRequest(new { message = "Invalid payment amount." });

        StripeConfiguration.ApiKey = secretKey;

        var intentService = new PaymentIntentService();
        var intent = await intentService.CreateAsync(new PaymentIntentCreateOptions
        {
            Amount = amount,
            Currency = "php",
            AutomaticPaymentMethods = new PaymentIntentAutomaticPaymentMethodsOptions { Enabled = true },
            Metadata = new Dictionary<string, string>
            {
                ["companyId"] = guest.CompanyId.ToString(CultureInfo.InvariantCulture),
                ["guestId"] = guest.GuestId.ToString(CultureInfo.InvariantCulture),
                ["reservationId"] = reservationId.ToString(CultureInfo.InvariantCulture),
                ["purpose"] = "balance"
            }
        }, cancellationToken: ct);

        if (string.IsNullOrWhiteSpace(intent.ClientSecret))
            return BadRequest(new { message = "Stripe did not return a client secret." });

        var payment = new Payment
        {
            CompanyId = guest.CompanyId,
            ReservationId = reservationId,
            Amount = balance,
            PaymentMethod = "Stripe",
            Status = "Pending",
            StripePaymentIntentId = intent.Id,
            CreatedAt = ViaReservaERP.AppTime.Now
        };

        _db.Payments.Add(payment);
        await _db.SaveChangesAsync(ct);

        return Ok(new
        {
            clientSecret = intent.ClientSecret,
            paymentIntentId = intent.Id,
            amount = balance.ToString("N2")
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ConfirmBalancePayment([FromBody] JsonElement body, CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);

        string? paymentIntentId = null;
        try
        {
            if (body.ValueKind == JsonValueKind.Object && body.TryGetProperty("paymentIntentId", out var piEl) && piEl.ValueKind == JsonValueKind.String)
                paymentIntentId = piEl.GetString();
        }
        catch { }

        if (string.IsNullOrWhiteSpace(paymentIntentId))
            return BadRequest(new { message = "Missing payment reference." });

        var secretKey = _config["Stripe:SecretKey"];
        if (string.IsNullOrWhiteSpace(secretKey))
            return BadRequest(new { message = "Stripe is not configured." });

        StripeConfiguration.ApiKey = secretKey;

        var intentService = new PaymentIntentService();
        var intent = await intentService.GetAsync(paymentIntentId, cancellationToken: ct);
        if (!string.Equals(intent.Status, "succeeded", StringComparison.OrdinalIgnoreCase))
            return BadRequest(new { message = $"Payment not successful (status: {intent.Status})." });

        var payment = await _db.Payments
            .OrderByDescending(p => p.PaymentId)
            .FirstOrDefaultAsync(p => p.CompanyId == guest.CompanyId && p.StripePaymentIntentId == paymentIntentId, ct);

        if (payment == null)
            return NotFound(new { message = "Payment record not found." });

        if (!payment.ReservationId.HasValue)
            return BadRequest(new { message = "Payment is not linked to a reservation." });

        var ownsReservation = await _db.Reservations
            .AsNoTracking()
            .AnyAsync(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId && r.ReservationId == payment.ReservationId.Value, ct);

        if (!ownsReservation)
            return Forbid();

        var alreadySucceeded = (payment.Status ?? string.Empty).ToLower().Contains("succeed");
        if (!alreadySucceeded)
        {
            payment.Status = "Succeeded";

            var paymentAmount = payment.Amount ?? 0m;
            var companyId = payment.CompanyId;

            var hasTxn = await _db.Transactions
                .AsNoTracking()
                .AnyAsync(t => t.CompanyId == companyId && t.ReferenceType == "Payment" && t.ReferenceId == payment.PaymentId, ct);

            if (!hasTxn)
            {
                var res = await _db.Reservations
                    .AsNoTracking()
                    .FirstOrDefaultAsync(r => r.CompanyId == guest.CompanyId && r.ReservationId == payment.ReservationId.Value, ct);

                var total = res?.TotalAmount ?? 0m;
                var ratio = total > 0m ? (paymentAmount / total) : 0m;

                _db.Transactions.Add(new AccountingTransaction
                {
                    CompanyId = companyId,
                    Subtotal = (res?.Subtotal ?? 0m) * ratio,
                    TaxAmount = (res?.TaxAmount ?? 0m) * ratio,
                    ServiceCharge = (res?.ServiceCharge ?? 0m) * ratio,
                    Amount = paymentAmount,
                    Type = "Income",
                    Description = $"Guest balance payment for Res #{payment.ReservationId} via Stripe",
                    ReferenceId = payment.PaymentId,
                    ReferenceType = "Payment",
                    TransactionDate = ViaReservaERP.AppTime.Now
                });
            }

            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = companyId,
                UserId = User.GetUserId(),
                Action = "Guest Balance Payment Succeeded",
                TableName = "Payments",
                RecordId = payment.PaymentId,
                NewValues = paymentIntentId,
                ActionDate = ViaReservaERP.AppTime.Now
            });

            await _db.SaveChangesAsync(ct);

            if (companyId.HasValue)
            {
                await _notify.NotifyRoleAsync(companyId.Value, roleId: 2,
                    title: "Payment Received",
                    message: $"Guest payment succeeded for Reservation #{payment.ReservationId}. Amount: {paymentAmount:N2}.",
                    type: "Payments",
                    ct);

                await _notify.NotifyRoleAsync(companyId.Value, roleId: 3,
                    title: "Payment Received",
                    message: $"Guest payment succeeded for Reservation #{payment.ReservationId}. Amount: {paymentAmount:N2}.",
                    type: "Payments",
                    ct);
            }
        }

        return Ok(new { ok = true });
    }

    private async Task<List<PaymentRow>> GetPaymentRowsAsync(Guest guest, CancellationToken ct)
    {
        var reservationIds = await _db.Reservations
            .AsNoTracking()
            .Where(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId)
            .Select(r => r.ReservationId)
            .ToListAsync(ct);

        return await _db.Payments
            .AsNoTracking()
            .Where(p => p.CompanyId == guest.CompanyId)
            .Where(p => p.ReservationId != null && reservationIds.Contains(p.ReservationId.Value))
            .OrderByDescending(p => p.PaymentId)
            .Select(p => new PaymentRow
            {
                PaymentId = p.PaymentId,
                ReservationId = p.ReservationId,
                Amount = p.Amount ?? 0m,
                Status = p.Status ?? "",
                PaymentMethod = p.PaymentMethod ?? "",
                CreatedAtUtc = p.CreatedAt,
                StripePaymentIntentId = p.StripePaymentIntentId ?? ""
            })
            .ToListAsync(ct);
    }

    [HttpGet]
    public async Task<IActionResult> PaymentsStats(CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);
        var rows = await GetPaymentRowsAsync(guest, ct);
        var totalPaid = rows.Where(p => (p.Status ?? "").ToLowerInvariant().Contains("succeed")).Sum(p => p.Amount);

        return Json(new { 
            totalPaid = totalPaid.ToString("N2"), 
            count = rows.Count 
        });
    }

    [HttpGet]
    public async Task<IActionResult> PaymentsTablePartial(CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);
        var rows = await GetPaymentRowsAsync(guest, ct);
        return PartialView("_PaymentsTableRows", rows);
    }

    [HttpGet]
    public async Task<IActionResult> DashboardStats(CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);

        var reservations = await _db.Reservations
            .AsNoTracking()
            .Where(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId)
            .ToListAsync(ct);

        bool IsCancelled(Reservation r) => (r.Status ?? "").Contains("Cancel", StringComparison.OrdinalIgnoreCase);
        bool IsCompleted(Reservation r) => (r.Status ?? "").Contains("Complete", StringComparison.OrdinalIgnoreCase) || (r.Status ?? "").Contains("Out", StringComparison.OrdinalIgnoreCase) || (r.CheckOutDate != null && r.CheckOutDate.Value < today && !IsCancelled(r));
        bool IsActive(Reservation r) => (r.Status ?? "").Contains("In", StringComparison.OrdinalIgnoreCase) && !(r.Status ?? "").Contains("Out", StringComparison.OrdinalIgnoreCase) && (r.CheckOutDate == null || r.CheckOutDate.Value >= today);
        bool IsUpcoming(Reservation r) => !IsCancelled(r) && !IsCompleted(r) && !IsActive(r);

        var upcoming = reservations.Count(IsUpcoming);
        var active = reservations.Count(IsActive);
        var completed = reservations.Count(IsCompleted);

        var pendingServices = await _db.ServiceRequests
            .AsNoTracking()
            .CountAsync(sr => sr.CompanyId == guest.CompanyId && sr.GuestId == guest.GuestId && (sr.Status == "Pending" || sr.Status == "Assigned" || sr.Status == "In Progress"), ct);

        var payments = await GetPaymentRowsAsync(guest, ct);
        var totalPaid = payments.Where(p => (p.Status ?? "").ToLowerInvariant().Contains("succeed")).Sum(p => p.Amount);

        return Json(new {
            upcoming,
            active,
            completed,
            pendingServices,
            totalPaid = totalPaid.ToString("N2")
        });
    }

    [HttpGet]
    public async Task<IActionResult> DashboardRecentPaymentsPartial(CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);
        var payments = await GetPaymentRowsAsync(guest, ct);
        var recent = payments.Take(5).ToList();
        return PartialView("_DashboardRecentPayments", recent);
    }

    [HttpGet]
    public async Task<IActionResult> DashboardRecentReservationsPartial(CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);
        var reservations = await _db.Reservations
            .AsNoTracking()
            .Where(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId)
            .OrderByDescending(r => r.ReservationId)
            .Take(8)
            .Select(r => new ReservationRow
            {
                ReservationId = r.ReservationId,
                Status = r.Status ?? "",
                CheckInDate = r.CheckInDate,
                CheckOutDate = r.CheckOutDate,
                RoomNumber = r.ReservationRooms.Select(rr => rr.Room!.RoomNumber).FirstOrDefault() ?? "",
                TotalAmount = r.TotalAmount ?? 0m
            })
            .ToListAsync(ct);

        return PartialView("_DashboardRecentReservations", reservations);
    }

    [HttpGet]
    public async Task<IActionResult> DashboardServiceRequestsPartial(CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);
        var recentRequests = await _db.ServiceRequests
            .AsNoTracking()
            .Where(sr => sr.CompanyId == guest.CompanyId && sr.GuestId == guest.GuestId)
            .Include(sr => sr.Service)
            .OrderByDescending(sr => sr.RequestDate)
            .Take(8)
            .Select(sr => new ServiceRequestRow
            {
                RequestId = sr.RequestId,
                ReservationId = sr.ReservationId,
                ServiceName = sr.Service != null ? sr.Service.ServiceName ?? "" : "",
                Price = sr.Service != null ? (sr.Service.Price ?? 0m) : 0m,
                Status = sr.Status ?? "",
                RequestedAtUtc = sr.RequestDate
            })
            .ToListAsync(ct);

        return PartialView("_DashboardServiceRequests", recentRequests);
    }

    [HttpGet]
    public async Task<IActionResult> Services(CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);

        var catalog = await _db.Services
            .AsNoTracking()
            .Where(s => s.CompanyId.HasValue && s.CompanyId.Value == guest.CompanyId)
            .OrderBy(s => s.ServiceName)
            .Select(s => new ServiceCatalogRow
            {
                ServiceId = s.ServiceId,
                ServiceName = s.ServiceName ?? "Service",
                Price = s.Price ?? 0m
            })
            .ToListAsync(ct);

        var requests = await _db.ServiceRequests
            .AsNoTracking()
            .Include(sr => sr.Service)
            .Where(sr => sr.CompanyId == guest.CompanyId && sr.GuestId == guest.GuestId)
            .OrderByDescending(sr => sr.RequestDate)
            .Take(120)
            .Select(sr => new ServiceRequestRow
            {
                RequestId = sr.RequestId,
                ReservationId = sr.ReservationId,
                ServiceName = sr.Service != null ? (sr.Service.ServiceName ?? "") : "",
                Price = sr.Service != null ? (sr.Service.Price ?? 0m) : 0m,
                Status = sr.Status ?? "",
                RequestedAtUtc = sr.RequestDate
            })
            .ToListAsync(ct);

        var model = new GuestServicesViewModel
        {
            GuestId = guest.GuestId,
            GuestName = guest.FullName ?? "",
            Catalog = catalog,
            Requests = requests
        };

        ViewData["Title"] = "Request Services";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> RequestService(int serviceId, CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var guest = await RequireCurrentGuestAsync(ct);

        var svcExists = await _db.Services
            .AsNoTracking()
            .AnyAsync(s => s.ServiceId == serviceId && s.CompanyId.HasValue && s.CompanyId.Value == guest.CompanyId, ct);
        if (!svcExists)
            return NotFound();

        var req = new ServiceRequest
        {
            CompanyId = guest.CompanyId,
            GuestId = guest.GuestId,
            ServiceId = serviceId,
            Status = "Pending",
            RequestDate = ViaReservaERP.AppTime.Now
        };
        _db.ServiceRequests.Add(req);

        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = guest.CompanyId,
            UserId = userId,
            Action = "Service Request Created",
            TableName = "ServiceRequests",
            RecordId = null,
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);

        var service = await _db.Services
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.ServiceId == serviceId, ct);
        var serviceName = service?.ServiceName ?? "Service";

        if (userId.HasValue)
        {
            await _notify.NotifyUserAsync(userId.Value,
                "Service Request Created",
                $"Request #{req.RequestId} created for {serviceName}. Status: Pending.",
                "Service",
                guest.CompanyId,
                ct);
        }

        if (!string.IsNullOrWhiteSpace(guest.Email))
        {
            await _notify.EmailUserAsync(
                guest.Email,
                $"ViaReserva: Service Request #{req.RequestId}",
                $"We received your service request for {serviceName}. Status: Pending.",
                html: null,
                ct);
        }

        await _notify.NotifyRoleAsync(guest.CompanyId, roleId: 5,
            title: "New Service Request",
            message: $"Guest {guest.FullName} created request #{req.RequestId} for {serviceName}.",
            type: "Service",
            ct);

        await _notify.NotifyRoleAsync(guest.CompanyId, roleId: 2,
            title: "Guest Service Request",
            message: $"Guest {guest.FullName} has requested {serviceName} (Request #{req.RequestId}).",
            type: "Service",
            ct);

        TempData["SuccessMessage"] = $"Request for {serviceName} has been submitted successfully.";
        return RedirectToAction(nameof(Services));
    }

    [HttpGet]
    public async Task<IActionResult> Tracking(int reservationId, CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);

        var reservationExists = await _db.Reservations
            .AsNoTracking()
            .AnyAsync(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId && r.ReservationId == reservationId, ct);
        if (!reservationExists)
            return NotFound();

        var wf = await _db.WorkflowInstances
            .AsNoTracking()
            .Include(w => w.Steps)
            .FirstOrDefaultAsync(w => w.CompanyId == guest.CompanyId && w.ReferenceType == "Reservation" && w.ReferenceId == reservationId, ct);

        var steps = wf?.Steps
            .OrderBy(s => s.InstanceStepId)
            .Select(s => new TrackingStepRow { Stage = s.ActionTaken ?? "", WhenUtc = s.PerformedAt })
            .ToList() ?? new();

        var reservation = await _db.Reservations
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.CompanyId == guest.CompanyId && r.GuestId == guest.GuestId && r.ReservationId == reservationId, ct);

        var model = new GuestTrackingViewModel
        {
            ReservationId = reservationId,
            Status = reservation?.Status ?? "",
            Steps = steps
        };

        ViewData["Title"] = "Tracking";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Profile(CancellationToken ct = default)
    {
        var guest = await RequireCurrentGuestAsync(ct);
        var userId = User.GetUserId() ?? 0;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.UserId == userId, ct);

        var model = new GuestProfileViewModel
        {
            GuestId = guest.GuestId,
            FullName = guest.FullName ?? "",
            Email = guest.Email ?? "",
            Phone = guest.Phone,
            AvatarUrl = user?.AvatarUrl
        };

        ViewData["Title"] = "Profile";
        return View(model);
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

    [HttpGet]
    public async Task<IActionResult> Notifications(CancellationToken ct = default)
    {
        var userId = User.GetUserId();
        var companyId = User.GetCompanyId();
        if (!userId.HasValue || !companyId.HasValue)
            return Forbid();

        var items = await _db.Notifications
            .AsNoTracking()
            .Where(n => n.UserId == userId.Value)
            .Where(n => n.CompanyId == companyId.Value)
            .OrderByDescending(n => n.CreatedAt)
            .Take(200)
            .Select(n => new NotificationRow
            {
                NotificationId = n.NotificationId,
                Title = n.Title ?? "",
                Message = n.Message ?? "",
                Type = n.Type ?? "",
                IsRead = n.IsRead,
                CreatedAtUtc = n.CreatedAt
            })
            .ToListAsync(ct);

        ViewData["Title"] = "Notifications";
        return View(new GuestNotificationsViewModel { Items = items });
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
            .CountAsync(n => n.UserId == userId.Value && n.CompanyId == companyId.Value && !n.IsRead, ct);

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
            .Where(n => n.UserId == userId.Value)
            .Where(n => n.CompanyId == companyId.Value)
            .OrderByDescending(n => n.CreatedAt)
            .Take(200)
            .Select(n => new NotificationRow
            {
                NotificationId = n.NotificationId,
                Title = n.Title ?? "",
                Message = n.Message ?? "",
                Type = n.Type ?? "",
                IsRead = n.IsRead,
                CreatedAtUtc = n.CreatedAt
            })
            .ToListAsync(ct);

        return PartialView("_NotificationsList", new GuestNotificationsViewModel { Items = items });
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
            .Where(n => n.UserId == userId.Value && n.CompanyId == companyId.Value && !n.IsRead)
            .ToListAsync(ct);

        foreach (var n in unread)
        {
            n.IsRead = true;
        }

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "All notifications marked as read.";
        return RedirectToAction(nameof(Notifications));
    }

    private async Task<(Payment? Payment, Reservation? Reservation, Company? Company)> GetInvoiceDataAsync(int paymentId, CancellationToken ct)
    {
        var guest = await RequireCurrentGuestAsync(ct);
        var payment = await _db.Payments
            .AsNoTracking()
            .Include(p => p.Reservation)
                .ThenInclude(r => r!.ReservationRooms)
                    .ThenInclude(rr => rr.Room)
                        .ThenInclude(room => room!.RoomType)
            .Include(p => p.Reservation)
                .ThenInclude(r => r!.ReservationServices)
                    .ThenInclude(rs => rs.Service)
            .Include(p => p.Reservation)
                .ThenInclude(r => r!.Guest)
            .FirstOrDefaultAsync(p => p.PaymentId == paymentId && p.CompanyId == guest.CompanyId, ct);

        if (payment == null || payment.Reservation == null || payment.Reservation.GuestId != guest.GuestId)
            return (null, null, null);

        var company = await _db.Companies.AsNoTracking().FirstOrDefaultAsync(c => c.CompanyId == guest.CompanyId, ct);
        return (payment, payment.Reservation, company);
    }

    [HttpGet]
    public async Task<IActionResult> DownloadInvoicePdf(int paymentId, bool inline = false, CancellationToken ct = default)
    {
        var (payment, reservation, company) = await GetInvoiceDataAsync(paymentId, ct);
        if (payment == null || reservation == null) return NotFound();

        var serviceRequests = await _db.ServiceRequests
            .AsNoTracking()
            .Include(sr => sr.Service)
            .Where(sr => sr.ReservationId == reservation.ReservationId)
            .OrderBy(sr => sr.RequestDate)
            .ToListAsync(ct);

        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(1, Unit.Centimetre);
                page.PageColor(Colors.White);
                page.DefaultTextStyle(x => x.FontSize(10).FontFamily("Arial"));

                page.Header().Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text(company?.CompanyName ?? "ViaReserva ERP").FontSize(20).SemiBold().FontColor(Colors.Blue.Medium);
                        col.Item().Text(company?.Address ?? "Hotel Receipt").FontSize(10).FontColor(Colors.Grey.Medium);
                    });

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("RECEIPT").FontSize(24).SemiBold().FontColor(Colors.Grey.Darken3).AlignRight();
                        col.Item().Text($"Ref: {payment.StripePaymentIntentId}").FontSize(8).AlignRight();
                        col.Item().Text($"Date: {payment.CreatedAt:MMM dd, yyyy HH:mm}").FontSize(8).AlignRight();
                    });
                });

                page.Content().PaddingVertical(10).Column(col =>
                {
                    var nights = (reservation.CheckOutDate.HasValue && reservation.CheckInDate.HasValue)
                        ? Math.Max(1, reservation.CheckOutDate.Value.DayNumber - reservation.CheckInDate.Value.DayNumber)
                        : 1;

                    // Calculate items subtotal
                    var roomsSubtotal = reservation.ReservationRooms.Sum(rr => (rr.Price ?? 0m) * nights);
                    var servicesSubtotal = reservation.ReservationServices.Sum(rs => (rs.Price ?? 0m) * (rs.Quantity ?? 1));
                    var requestedServicesSubtotal = serviceRequests.Where(sr => sr.Status == "Completed").Sum(sr => sr.Service?.Price ?? 0m);
                    var calculatedSubtotal = roomsSubtotal + servicesSubtotal + requestedServicesSubtotal;

                    // Determine which subtotal to use (prefer stored if it exists, but show adjustments)
                    var finalSubtotal = reservation.Subtotal ?? calculatedSubtotal;
                    var adjustment = finalSubtotal - calculatedSubtotal;

                    var hasStoredTax = (reservation.Subtotal.HasValue && reservation.Subtotal.Value > 0m)
                        || (reservation.ServiceCharge.HasValue && reservation.ServiceCharge.Value > 0m)
                        || (reservation.TaxAmount.HasValue && reservation.TaxAmount.Value > 0m);

                    var tax = hasStoredTax
                        ? new TaxResult
                        {
                            Subtotal = finalSubtotal,
                            ServiceCharge = reservation.ServiceCharge ?? 0m,
                            TaxAmount = reservation.TaxAmount ?? 0m,
                            Total = (reservation.Subtotal ?? finalSubtotal) + (reservation.ServiceCharge ?? 0m) + (reservation.TaxAmount ?? 0m)
                        }
                        : _tax.CalculateTaxes(finalSubtotal, reservation.CompanyId);

                    col.Item().PaddingTop(10).Row(row =>
                    {
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Guest Details").SemiBold();
                            c.Item().Text(reservation.Guest?.FullName ?? "Valued Guest");
                            c.Item().Text(reservation.Guest?.Email ?? "");
                        });
                        row.RelativeItem().Column(c =>
                        {
                            c.Item().Text("Reservation Info").SemiBold().AlignRight();
                            c.Item().Text($"Booking #{reservation.ReservationId}").AlignRight();
                            c.Item().Text($"Check-in: {reservation.CheckInDate:MMM dd, yyyy}").AlignRight();
                            c.Item().Text($"Check-out: {reservation.CheckOutDate:MMM dd, yyyy}").AlignRight();
                        });
                    });

                    col.Item().PaddingTop(20).Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn(3);
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Header(header =>
                        {
                            header.Cell().Element(CellStyle).Text("Description");
                            header.Cell().Element(CellStyle).AlignRight().Text("Qty");
                            header.Cell().Element(CellStyle).AlignRight().Text("Amount");

                            static IContainer CellStyle(IContainer container)
                            {
                                return container.DefaultTextStyle(x => x.SemiBold()).PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Black);
                            }
                        });

                        foreach (var rr in reservation.ReservationRooms)
                        {
                            var dailyRate = rr.Price ?? 0m;
                            var roomTotal = dailyRate * nights;
                            table.Cell().Element(CellStyle).Text($"{rr.Room?.RoomType?.TypeName} (Room {rr.Room?.RoomNumber}) - {nights} nights @ ₱ {dailyRate:N2}");
                            table.Cell().Element(CellStyle).AlignRight().Text(nights.ToString());
                            table.Cell().Element(CellStyle).AlignRight().Text($"₱ {roomTotal:N2}");
                        }

                        foreach (var rs in reservation.ReservationServices)
                        {
                            table.Cell().Element(CellStyle).Text(rs.Service?.ServiceName ?? "Service");
                            table.Cell().Element(CellStyle).AlignRight().Text((rs.Quantity ?? 1).ToString());
                            table.Cell().Element(CellStyle).AlignRight().Text($"₱ {(rs.Price ?? 0m):N2}");
                        }

                        if (adjustment != 0)
                        {
                            table.Cell().Element(CellStyle).Text(adjustment > 0 ? "Stay Adjustments / Additional Charges" : "Stay Adjustments / Discounts");
                            table.Cell().Element(CellStyle).AlignRight().Text("1");
                            table.Cell().Element(CellStyle).AlignRight().Text($"₱ {adjustment:N2}");
                        }

                        static IContainer CellStyle(IContainer container) => container.PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
                    });

                    if (serviceRequests.Any())
                    {
                        col.Item().PaddingTop(20).Text("Requested Services").SemiBold();
                        col.Item().PaddingTop(5).Table(table =>
                        {
                            table.ColumnsDefinition(columns =>
                            {
                                columns.RelativeColumn(3);
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                                columns.RelativeColumn();
                            });

                            table.Header(header =>
                            {
                                header.Cell().Element(CellStyle).Text("Service");
                                header.Cell().Element(CellStyle).AlignRight().Text("Requested At");
                                header.Cell().Element(CellStyle).AlignRight().Text("Status");
                                header.Cell().Element(CellStyle).AlignRight().Text("Price");

                                static IContainer CellStyle(IContainer container)
                                {
                                    return container.DefaultTextStyle(x => x.SemiBold()).PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Black);
                                }
                            });

                            foreach (var sr in serviceRequests)
                            {
                                table.Cell().Element(CellStyle).Text(sr.Service?.ServiceName ?? "Service");
                                table.Cell().Element(CellStyle).AlignRight().Text($"{sr.RequestDate:MMM dd HH:mm}");
                                table.Cell().Element(CellStyle).AlignRight().Text(sr.Status ?? "Pending");
                                table.Cell().Element(CellStyle).AlignRight().Text($"₱ {(sr.Service?.Price ?? 0m):N2}");
                            }

                            static IContainer CellStyle(IContainer container) => container.PaddingVertical(5).BorderBottom(1).BorderColor(Colors.Grey.Lighten2);
                        });
                    }

                    col.Item().AlignRight().PaddingTop(20).Column(c =>
                    {
                        c.Item().Text($"Subtotal: ₱ {tax.Subtotal:N2}").FontSize(10);
                        if (tax.ServiceCharge > 0)
                            c.Item().Text($"Service Charge (10%): ₱ {tax.ServiceCharge:N2}").FontSize(10);
                        if (tax.TaxAmount > 0)
                            c.Item().Text($"VAT (12%): ₱ {tax.TaxAmount:N2}").FontSize(10);

                        c.Item().PaddingTop(5).Text($"Total Amount: ₱ {tax.Total:N2}").FontSize(14).SemiBold();
                        c.Item().PaddingTop(5).Text($"Paid via {payment.PaymentMethod}").FontSize(10).Italic();
                        c.Item().Text($"Payment Status: {payment.Status}").FontSize(10).Italic();
                    });
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

        var stream = new MemoryStream();
        document.GeneratePdf(stream);
        stream.Position = 0;

        if (inline)
        {
            return File(stream, "application/pdf");
        }

        return File(stream, "application/pdf", $"Receipt_{paymentId}.pdf");
    }

    [HttpGet]
    public async Task<IActionResult> DownloadInvoiceExcel(int paymentId, CancellationToken ct = default)
    {
        var (payment, reservation, company) = await GetInvoiceDataAsync(paymentId, ct);
        if (payment == null || reservation == null) return NotFound();

        using var workbook = new XLWorkbook();
        var worksheet = workbook.Worksheets.Add("Receipt");

        worksheet.Cell(1, 1).Value = company?.CompanyName ?? "ViaReserva ERP";
        worksheet.Cell(1, 1).Style.Font.Bold = true;
        worksheet.Cell(1, 1).Style.Font.FontSize = 16;

        worksheet.Cell(2, 1).Value = company?.Address ?? "";
        
        worksheet.Cell(4, 1).Value = "RECEIPT";
        worksheet.Cell(4, 1).Style.Font.Bold = true;

        worksheet.Cell(5, 1).Value = "Reference:";
        worksheet.Cell(5, 2).Value = payment.StripePaymentIntentId;

        worksheet.Cell(6, 1).Value = "Date:";
        worksheet.Cell(6, 2).Value = payment.CreatedAt.ToString("MMM dd, yyyy");

        worksheet.Cell(8, 1).Value = "Guest:";
        worksheet.Cell(8, 2).Value = reservation.Guest?.FullName;

        worksheet.Cell(9, 1).Value = "Reservation #:";
        worksheet.Cell(9, 2).Value = reservation.ReservationId;

        worksheet.Cell(11, 1).Value = "Description";
        worksheet.Cell(11, 2).Value = "Amount";
        worksheet.Range(11, 1, 11, 2).Style.Font.Bold = true;
        worksheet.Range(11, 1, 11, 2).Style.Fill.BackgroundColor = XLColor.LightGray;

        int row = 12;
        foreach (var rr in reservation.ReservationRooms)
        {
            worksheet.Cell(row, 1).Value = $"{rr.Room?.RoomType?.TypeName} (Room {rr.Room?.RoomNumber})";
            worksheet.Cell(row, 2).Value = rr.Price ?? 0m;
            worksheet.Cell(row, 2).Style.NumberFormat.Format = "₱ #,##0.00";
            row++;
        }

        foreach (var rs in reservation.ReservationServices)
        {
            worksheet.Cell(row, 1).Value = rs.Service?.ServiceName ?? "Service";
            worksheet.Cell(row, 2).Value = rs.Price ?? 0m;
            worksheet.Cell(row, 2).Style.NumberFormat.Format = "₱ #,##0.00";
            row++;
        }

        row++;
        worksheet.Cell(row, 1).Value = "Subtotal:";
        worksheet.Cell(row, 2).Value = reservation.Subtotal ?? (reservation.TotalAmount ?? 0m);
        worksheet.Cell(row, 2).Style.NumberFormat.Format = "₱ #,##0.00";
        row++;

        if ((reservation.ServiceCharge ?? 0m) > 0)
        {
            worksheet.Cell(row, 1).Value = "Service Charge (10%):";
            worksheet.Cell(row, 2).Value = reservation.ServiceCharge ?? 0m;
            worksheet.Cell(row, 2).Style.NumberFormat.Format = "₱ #,##0.00";
            row++;
        }

        if ((reservation.TaxAmount ?? 0m) > 0)
        {
            worksheet.Cell(row, 1).Value = "VAT (12%):";
            worksheet.Cell(row, 2).Value = reservation.TaxAmount ?? 0m;
            worksheet.Cell(row, 2).Style.NumberFormat.Format = "₱ #,##0.00";
            row++;
        }

        row++;
        worksheet.Cell(row, 1).Value = "Total Amount:";
        worksheet.Cell(row, 1).Style.Font.Bold = true;
        worksheet.Cell(row, 2).Value = reservation.TotalAmount ?? 0m;
        worksheet.Cell(row, 2).Style.Font.Bold = true;
        worksheet.Cell(row, 2).Style.NumberFormat.Format = "₱ #,##0.00";

        worksheet.Columns().AdjustToContents();

        using var stream = new MemoryStream();
        workbook.SaveAs(stream);
        stream.Position = 0;
        return File(stream, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"Receipt_{paymentId}.xlsx");
    }
}
