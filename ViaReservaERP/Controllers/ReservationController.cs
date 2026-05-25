using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using ViaReservaERP.Data;
using ViaReservaERP.Models;
using ViaReservaERP.Models.Reservations;
using ViaReservaERP.Services;
using ViaReservaERP.Security;

namespace ViaReservaERP.Controllers;

public class ReservationController : Controller
{
    private readonly ViaReservaDbContext _db;
    private readonly IAuthSignInService _authSignInService;
    private readonly IStripePaymentService _stripe;
    private readonly IBookingCheckoutService _checkout;
    private readonly IConfiguration _config;
    private readonly INotificationService _notification;
    private readonly IEmailTemplateService _templates;

    public ReservationController(
        ViaReservaDbContext db,
        IAuthSignInService authSignInService,
        IStripePaymentService stripe,
        IBookingCheckoutService checkout,
        IConfiguration config,
        INotificationService notification,
        IEmailTemplateService templates)
    {
        _db = db;
        _authSignInService = authSignInService;
        _stripe = stripe;
        _checkout = checkout;
        _config = config;
        _notification = notification;
        _templates = templates;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Availability(DateOnly checkInDate, DateOnly checkOutDate, CancellationToken ct = default)
    {
        await _checkout.SyncRoomStatusesAsync(null, ct);
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);
        if (checkInDate < today)
            return BadRequest(new { message = "Check-in date cannot be in the past." });

        if (checkOutDate <= checkInDate)
            return BadRequest(new { message = "Check-out must be after check-in." });

        var companies = await _db.Companies
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsActive)
            .Where(c => (c.SubscriptionStatus ?? "").ToLower() == "active")
            .OrderBy(c => c.CompanyName)
            .Select(c => new
            {
                c.CompanyId,
                name = c.CompanyName,
                address = c.Address ?? string.Empty,
                phone = c.Phone ?? string.Empty
            })
            .ToListAsync(ct);

        if (companies.Count == 0)
            return Ok(new { companies = Array.Empty<object>(), roomTypes = Array.Empty<object>(), roomAvailability = Array.Empty<object>(), services = Array.Empty<object>() });

        var companyIds = companies.Select(c => c.CompanyId).ToList();

        var overlappingReservationIds = await _db.Reservations
            .AsNoTracking()
            .Where(r => companyIds.Contains(r.CompanyId))
            .Where(r => r.CheckInDate != null && r.CheckOutDate != null)
            .Where(r => r.CheckInDate.Value < checkOutDate && r.CheckOutDate.Value > checkInDate)
            .Where(r => r.Status == null || !r.Status.ToLower().Contains("cancel"))
            .Select(r => r.ReservationId)
            .ToListAsync(ct);

        var reservedRoomIds = await _db.ReservationRooms
            .AsNoTracking()
            .Where(rr => rr.ReservationId != null && overlappingReservationIds.Contains(rr.ReservationId.Value))
            .Where(rr => rr.RoomId != null)
            .Select(rr => rr.RoomId!.Value)
            .Distinct()
            .ToListAsync(ct);

        var roomTypeRows = await _db.RoomTypes
            .AsNoTracking()
            .Where(rt => companyIds.Contains(rt.CompanyId))
            .Select(rt => new
            {
                rt.CompanyId,
                rt.RoomTypeId,
                typeName = rt.TypeName ?? "Room",
                basePrice = rt.BasePrice ?? 0m
            })
            .ToListAsync(ct);

        var roomAvailabilityRows = await _db.Rooms
            .AsNoTracking()
            .Where(r => companyIds.Contains(r.CompanyId))
            .Where(r => r.Status == "Available")
            .Where(r => !reservedRoomIds.Contains(r.RoomId))
            .Select(r => new
            {
                companyId = r.CompanyId,
                roomId = r.RoomId,
                roomTypeId = r.RoomTypeId,
                roomNumber = r.RoomNumber ?? "Room"
            })
            .ToListAsync(ct);

        var services = await _db.Services
            .AsNoTracking()
            .Where(s => s.CompanyId.HasValue && companyIds.Contains(s.CompanyId.Value))
            .Select(s => new
            {
                companyId = s.CompanyId!.Value,
                s.ServiceId,
                name = s.ServiceName ?? "Service",
                price = s.Price ?? 0m
            })
            .OrderBy(s => s.name)
            .ToListAsync(ct);

        return Ok(new
        {
            companies = companies,
            roomTypes = roomTypeRows,
            roomAvailability = roomAvailabilityRows,
            services = services
        });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Book(CancellationToken ct = default)
    {
        ViewData["StripePublishableKey"] = _config["Stripe:PublishableKey"] ?? string.Empty;

        var companies = await _db.Companies
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.CompanyName)
            .ToListAsync(ct);

        ViewData["Companies"] = companies;
        await RebuildBookingUiDataAsync(companies, ct);

        var model = new BookReservationViewModel
        {
            CheckInDate = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date),
            CheckOutDate = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date.AddDays(1))
        };

        var userId = User.GetUserId();
        if (userId.HasValue)
        {
            var user = await _db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.UserId == userId.Value, ct);
            if (user != null)
            {
                model.FullName = user.FullName;
                model.Email = user.Email;
                model.Phone = user.Phone;
            }
        }

        return View(model);
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreatePaymentIntent([FromBody] CreatePaymentIntentRequest request, CancellationToken ct = default)
    {
        if (!ModelState.IsValid)
            return BadRequest(new { message = "Invalid payment request." });

        try
        {
            var result = await _stripe.CreateReservationPaymentIntentAsync(request, ct);
            return Ok(result);
        }
        catch (Exception ex)
        {
            return BadRequest(new { message = ex.Message });
        }
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Book(BookReservationViewModel model, CancellationToken ct = default)
    {
        ViewData["StripePublishableKey"] = _config["Stripe:PublishableKey"] ?? string.Empty;

        var companies = await _db.Companies
            .AsNoTracking()
            .Where(c => !c.IsDeleted && c.IsActive)
            .OrderBy(c => c.CompanyName)
            .ToListAsync(ct);

        ViewData["Companies"] = companies;

        // Always rebuild BookingUiDataJson so the wizard JavaScript works if we need to return the view.
        await RebuildBookingUiDataAsync(companies, ct);

        if (!ModelState.IsValid)
        {
            return View(model);
        }

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);
        if (model.CheckInDate < today)
        {
            ModelState.AddModelError(nameof(BookReservationViewModel.CheckInDate), "Check-in date cannot be in the past.");
            return View(model);
        }

        if (model.CheckOutDate <= model.CheckInDate)
        {
            ModelState.AddModelError(nameof(BookReservationViewModel.CheckOutDate), "Check-out must be after check-in.");
            return View(model);
        }

        var authenticatedUserId = User.GetUserId();
        if (!authenticatedUserId.HasValue && string.IsNullOrWhiteSpace(model.Password))
        {
            ModelState.AddModelError(nameof(BookReservationViewModel.Password), "Password is required to create your guest portal account.");
            return View(model);
        }

        var companyExists = companies.Any(c => c.CompanyId == model.CompanyId);
        if (!companyExists)
        {
            return NotFound();
        }

        try
        {
            var reservationId = await _checkout.FinalizeReservationBookingAsync(model, authenticatedUserId, ct);

            // Fetch reservation details for the email
            var reservation = await _db.Reservations
                .AsNoTracking()
                .Include(r => r.Company)
                .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room).ThenInclude(r => r.RoomType)
                .Include(r => r.Payments)
                .FirstOrDefaultAsync(r => r.ReservationId == reservationId, ct);

            if (reservation != null)
            {
                var roomType = reservation.ReservationRooms.FirstOrDefault()?.Room?.RoomType?.TypeName ?? "Standard Room";
                var paymentStatus = reservation.Payments.OrderByDescending(p => p.PaymentId).Select(p => p.Status).FirstOrDefault() ?? "Pending";
                
                var (plain, html) = _templates.GetReservationConfirmationTemplate(
                    model.FullName,
                    reservationId.ToString(),
                    roomType,
                    reservation.CheckInDate?.ToString("MMM dd, yyyy") ?? model.CheckInDate.ToString("MMM dd, yyyy"),
                    reservation.CheckOutDate?.ToString("MMM dd, yyyy") ?? model.CheckOutDate.ToString("MMM dd, yyyy"),
                    $"₱{reservation.TotalAmount ?? 0m:N2}",
                    paymentStatus
                );

                await _notification.EmailUserAsync(model.Email, "Booking Confirmation - ViaReserva", plain, html, ct);
            }

            var user = await _db.Users
                .AsNoTracking()
                .FirstOrDefaultAsync(u => u.Email == model.Email && !u.IsDeleted && u.IsActive, ct);

            if (user != null)
                await _authSignInService.SignInAsync(HttpContext, user, ct);

            return RedirectToAction("Dashboard", "Guest", new { reservationId });
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            ModelState.AddModelError(string.Empty, "Booking Error: " + msg);
            return View(model);
        }
    }

    private async Task RebuildBookingUiDataAsync(List<ViaReservaERP.Models.Company> companies, CancellationToken ct)
    {
        await _checkout.SyncRoomStatusesAsync(null, ct);
        var companyIds = companies.Select(c => c.CompanyId).ToList();

        var roomTypeRows = await _db.RoomTypes
            .AsNoTracking()
            .Where(rt => companyIds.Contains(rt.CompanyId))
            .Select(rt => new
            {
                rt.CompanyId,
                rt.RoomTypeId,
                TypeName = rt.TypeName ?? "Room",
                BasePrice = rt.BasePrice ?? 0m
            })
            .ToListAsync(ct);

        var roomAvailabilityRows = await _db.Rooms
            .AsNoTracking()
            .Where(r => companyIds.Contains(r.CompanyId) && r.Status == "Available")
            .Select(r => new
            {
                r.CompanyId,
                r.RoomId,
                r.RoomTypeId,
                RoomNumber = r.RoomNumber ?? "Room"
            })
            .ToListAsync(ct);

        var services = await _db.Services
            .AsNoTracking()
            .Where(s => s.CompanyId.HasValue && companyIds.Contains(s.CompanyId.Value))
            .Select(s => new
            {
                CompanyId = s.CompanyId!.Value,
                s.ServiceId,
                Name = s.ServiceName ?? "Service",
                Price = s.Price ?? 0m
            })
            .OrderBy(s => s.Name)
            .ToListAsync(ct);

        var bookingUiData = new
        {
            companies = companies.Select(c => new
            {
                c.CompanyId,
                name = c.CompanyName,
                address = c.Address ?? string.Empty,
                phone = c.Phone ?? string.Empty
            }),
            roomTypes = roomTypeRows,
            roomAvailability = roomAvailabilityRows,
            services
        };

        ViewData["BookingUiDataJson"] = JsonSerializer.Serialize(
            bookingUiData,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Confirmation(int id, CancellationToken ct = default)
    {
        var reservation = await _db.Reservations
            .AsNoTracking()
            .Include(r => r.Company)
            .Include(r => r.Guest)
            .Include(r => r.Payments)
            .FirstOrDefaultAsync(r => r.ReservationId == id, ct);

        if (reservation is null)
            return NotFound();

        ViewData["ReservationId"] = reservation.ReservationId;
        ViewData["Company"] = reservation.Company != null ? reservation.Company.CompanyName : string.Empty;
        ViewData["Amount"] = reservation.TotalAmount ?? 0m;
        ViewData["PaymentStatus"] = reservation.Payments.OrderByDescending(p => p.PaymentId).Select(p => p.Status).FirstOrDefault() ?? "";
        ViewData["Email"] = reservation.Guest != null ? (reservation.Guest.Email ?? "") : "";

        return View();
    }
}
