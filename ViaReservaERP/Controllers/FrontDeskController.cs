using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using ViaReservaERP.Data;
using ViaReservaERP.Models;
using ViaReservaERP.Models.Admin;
using ViaReservaERP.Models.FrontDesk;
using ViaReservaERP.Security;
using ViaReservaERP.Services;

namespace ViaReservaERP.Controllers;

[Authorize(Policy = RoleNames.FrontDesk)]
public class FrontDeskController : Controller
{
    private readonly ViaReservaDbContext _db;
    private readonly ITaxService _tax;
    private readonly IWebHostEnvironment _env;
    private readonly IBookingCheckoutService _checkout;

    public FrontDeskController(ViaReservaDbContext db, ITaxService tax, IWebHostEnvironment env, IBookingCheckoutService checkout)
    {
        _db = db;
        _tax = tax;
        _env = env;
        _checkout = checkout;
    }

    private int CurrentCompanyId => User.GetCompanyId() ?? 0;

    private AuditLog NewAudit(string action, string tableName, int? recordId, string? newValues)
    {
        return new AuditLog
        {
            CompanyId = CurrentCompanyId,
            UserId = User.GetUserId(),
            Action = action,
            TableName = tableName,
            RecordId = recordId,
            NewValues = newValues,
            IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers["User-Agent"].ToString(),
            ActionDate = DateTime.SpecifyKind(ViaReservaERP.AppTime.Now, DateTimeKind.Utc)
        };
    }

    public async Task<IActionResult> Dashboard(CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        await _checkout.SyncRoomStatusesAsync(companyId, ct);
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);

        var reservationsToday = await _db.Reservations
            .CountAsync(r => r.CompanyId == companyId && (r.CheckInDate == today || r.CheckOutDate == today), ct);

        var checkInsToday = await _db.Reservations
            .CountAsync(r => r.CompanyId == companyId && r.CheckInDate == today, ct);

        var checkOutsToday = await _db.Reservations
            .CountAsync(r => r.CompanyId == companyId && r.CheckOutDate == today, ct);

        var inHouseGuests = await _db.ReservationRooms
            .Include(rr => rr.Reservation)
            .Where(rr => rr.Reservation != null && rr.Reservation.CompanyId == companyId && rr.Reservation.Status == "Checked In")
            .Select(rr => rr.ReservationId)
            .Distinct()
            .CountAsync(ct);

        var recentReservations = await _db.Reservations
            .Include(r => r.Guest)
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.ReservationId)
            .Take(5)
            .Select(r => new ReservationSummary
            {
                ReservationId = r.ReservationId,
                GuestName = r.Guest != null ? r.Guest.FullName : "Unknown",
                Status = r.Status != null ? r.Status : "Pending",
                Date = r.CreatedAt,
                Amount = r.TotalAmount.HasValue ? r.TotalAmount.Value : 0m
            })
            .ToListAsync(ct);

        var model = new FrontDeskDashboardViewModel
        {
            ReservationsToday = reservationsToday,
            CheckInsToday = checkInsToday,
            CheckOutsToday = checkOutsToday,
            InHouseGuests = inHouseGuests,
            RecentReservations = recentReservations
        };

        ViewData["Title"] = "Front Desk Dashboard";
        return View(model);
    }

    public async Task<IActionResult> Reservations(string? searchGuest, string? statusFilter, DateOnly? startDate, DateOnly? endDate,
        string sort = "id", string dir = "desc", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        await _checkout.SyncRoomStatusesAsync(companyId, ct);
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);

        if (!startDate.HasValue) startDate = new DateOnly(today.Year, today.Month, 1);
        if (!endDate.HasValue) endDate = new DateOnly(today.Year, today.Month, DateTime.DaysInMonth(today.Year, today.Month));

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

        query = sort.ToLowerInvariant() switch
        {
            "id" => dir == "asc" ? query.OrderBy(r => r.ReservationId) : query.OrderByDescending(r => r.ReservationId),
            "guest" => dir == "asc" ? query.OrderBy(r => r.Guest.FullName) : query.OrderByDescending(r => r.Guest.FullName),
            "checkin" => dir == "asc" ? query.OrderBy(r => r.CheckInDate) : query.OrderByDescending(r => r.CheckInDate),
            "status" => dir == "asc" ? query.OrderBy(r => r.Status) : query.OrderByDescending(r => r.Status),
            _ => query.OrderByDescending(r => r.ReservationId)
        };

        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;

        var totalRows = await query.CountAsync(ct);
        var rows = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var baseStats = _db.Reservations.Where(r => r.CompanyId == companyId);

        var model = new ReservationsViewModel
        {
            Rows = rows,
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
            CheckOutsToday = await baseStats.CountAsync(r => r.CheckOutDate == today, ct)
        };

        ViewData["Title"] = "Reservation Management";
        return View(model);
    }

    [HttpGet]
    public async Task<IActionResult> Create(CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        await _checkout.SyncRoomStatusesAsync(companyId, ct);

        var guests = await _db.Guests
            .Where(g => g.CompanyId == companyId && !g.IsDeleted)
            .OrderBy(g => g.FullName)
            .Select(g => new SelectListItem { Value = g.GuestId.ToString(), Text = g.FullName })
            .ToListAsync(ct);

        var rooms = await _db.Rooms
            .Include(r => r.RoomType)
            .Where(r => r.CompanyId == companyId && r.Status == "Available" && !r.IsDeleted)
            .Select(r => new RoomSelectionItem
            {
                RoomId = r.RoomId,
                RoomNumber = r.RoomNumber != null ? r.RoomNumber : "N/A",
                TypeName = r.RoomType != null ? r.RoomType.TypeName : "Standard",
                BasePrice = r.RoomType != null && r.RoomType.BasePrice.HasValue ? r.RoomType.BasePrice.Value : 0m
            })
            .ToListAsync(ct);

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);
        var model = new WalkInReservationViewModel
        {
            Guests = guests,
            AvailableRooms = rooms,
            CheckInDate = today,
            CheckOutDate = today.AddDays(1)
        };

        ViewData["Title"] = "Walk-in Reservation";
        return View(model);
    }

    [HttpGet]
    public IActionResult TaxQuote(decimal baseAmount)
    {
        var companyId = CurrentCompanyId;
        if (companyId <= 0) return BadRequest(new { message = "Missing company context." });
        if (baseAmount < 0) return BadRequest(new { message = "Invalid amount." });

        var taxes = _tax.CalculateTaxes(baseAmount, companyId);
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
    public async Task<IActionResult> Create(WalkInReservationViewModel model, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);

        if (model.CheckInDate < today)
        {
            ModelState.AddModelError(nameof(WalkInReservationViewModel.CheckInDate), "Check-in date cannot be in the past.");
        }

        if (model.SelectedGuestId == null && string.IsNullOrWhiteSpace(model.NewGuestFullName))
        {
            ModelState.AddModelError(string.Empty, "Please select a guest or enter new guest details.");
        }

        if (model.CheckOutDate <= model.CheckInDate)
        {
            ModelState.AddModelError(nameof(WalkInReservationViewModel.CheckOutDate), "Check-out must be after check-in.");
        }

        if (model.SelectedRoomId <= 0)
        {
            ModelState.AddModelError(nameof(WalkInReservationViewModel.SelectedRoomId), "Please select an available room.");
        }

        if (!ModelState.IsValid)
        {
            await RebuildCreateListsAsync(model, companyId, ct);
            ViewData["Title"] = "Walk-in Reservation";
            return View(model);
        }

        await using var tx = await _db.Database.BeginTransactionAsync(ct);
        try
        {
            int guestId;
            if (model.SelectedGuestId.HasValue)
            {
                var guestExists = await _db.Guests.AnyAsync(g => g.CompanyId == companyId && g.GuestId == model.SelectedGuestId.Value && !g.IsDeleted, ct);
                if (!guestExists)
                {
                    TempData["ErrorMessage"] = "Selected guest not found.";
                    return RedirectToAction(nameof(Create));
                }

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
                    CreatedAt = ViaReservaERP.AppTime.Now,
                    IsActive = true,
                    IsDeleted = false
                };

                _db.Guests.Add(newGuest);
                await _db.SaveChangesAsync(ct);
                guestId = newGuest.GuestId;

                if (model.CreatePortalAccount)
                {
                    if (string.IsNullOrWhiteSpace(newGuest.Email))
                    {
                        TempData["ErrorMessage"] = "Guest email is required to create a portal account.";
                        return RedirectToAction(nameof(Create));
                    }

                    if (string.IsNullOrWhiteSpace(model.NewGuestPassword))
                    {
                        TempData["ErrorMessage"] = "Password is required to create a portal account.";
                        return RedirectToAction(nameof(Create));
                    }

                    var email = newGuest.Email.Trim().ToLowerInvariant();

                    var existingUser = await _db.Users
                        .FirstOrDefaultAsync(u => u.Email == email && !u.IsDeleted, ct);

                    if (existingUser == null)
                    {
                        var guestUser = new ErpUser
                        {
                            CompanyId = companyId,
                            RoleId = 6,
                            FullName = newGuest.FullName,
                            Email = email,
                            PasswordHash = PasswordHasher.Hash(model.NewGuestPassword.Trim()),
                            IsActive = true,
                            IsDeleted = false,
                            CreatedAt = ViaReservaERP.AppTime.Now
                        };

                        _db.Users.Add(guestUser);
                        await _db.SaveChangesAsync(ct);

                        newGuest.UserId = guestUser.UserId;
                        await _db.SaveChangesAsync(ct);

                        _db.AuditLogs.Add(NewAudit(
                            action: "Guest Registration",
                            tableName: "Users",
                            recordId: guestUser.UserId,
                            newValues: $"Guest portal account created: {guestUser.Email}"));
                    }
                    else
                    {
                        // If this email is already registered, link the Guest profile to it.
                        newGuest.UserId = existingUser.UserId;
                        await _db.SaveChangesAsync(ct);
                    }
                }

                _db.AuditLogs.Add(NewAudit(
                    action: "Insert",
                    tableName: "Guests",
                    recordId: guestId,
                    newValues: $"Walk-in guest created: {newGuest.FullName} ({newGuest.Email})"));
            }

            var room = await _db.Rooms
                .Include(r => r.RoomType)
                .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.RoomId == model.SelectedRoomId && !r.IsDeleted, ct);

            if (room == null || !string.Equals(room.Status, "Available", StringComparison.OrdinalIgnoreCase))
            {
                TempData["ErrorMessage"] = "The selected room is no longer available.";
                return RedirectToAction(nameof(Create));
            }

            var nights = Math.Max(1, (model.CheckOutDate.ToDateTime(TimeOnly.MinValue) - model.CheckInDate.ToDateTime(TimeOnly.MinValue)).Days);
            var basePrice = room.RoomType?.BasePrice ?? 0m;
            var baseAmount = basePrice * nights;
            var taxResult = _tax.CalculateTaxes(baseAmount, companyId);
            var totalAmount = taxResult.Total;

            var reservation = new Reservation
            {
                CompanyId = companyId,
                GuestId = guestId,
                CheckInDate = model.CheckInDate,
                CheckOutDate = model.CheckOutDate,
                Status = model.CheckInNow ? "Checked In" : "Confirmed",
                Subtotal = taxResult.Subtotal,
                TaxAmount = taxResult.TaxAmount,
                ServiceCharge = taxResult.ServiceCharge,
                TotalAmount = totalAmount,
                CreatedAt = ViaReservaERP.AppTime.Now
            };

            _db.Reservations.Add(reservation);
            await _db.SaveChangesAsync(ct);

            _db.AuditLogs.Add(NewAudit(
                action: "Insert",
                tableName: "Reservations",
                recordId: reservation.ReservationId,
                newValues: $"Walk-in reservation created for GuestId={guestId}, RoomId={room.RoomId}, {model.CheckInDate:yyyy-MM-dd} to {model.CheckOutDate:yyyy-MM-dd}, Status={reservation.Status}"));

            _db.ReservationRooms.Add(new ReservationRoom
            {
                ReservationId = reservation.ReservationId,
                RoomId = room.RoomId,
                Price = basePrice
            });

            room.Status = model.CheckInNow ? "Occupied" : "Reserved";

            var guestUserId = await _db.Guests
                .AsNoTracking()
                .Where(g => g.CompanyId == companyId && g.GuestId == guestId && !g.IsDeleted)
                .Select(g => g.UserId)
                .FirstOrDefaultAsync(ct);

            if (guestUserId.HasValue)
            {
                _db.Notifications.Add(new Notification
                {
                    UserId = guestUserId.Value,
                    CompanyId = companyId,
                    Title = "Reservation Confirmed",
                    Message = $"Reservation #{reservation.ReservationId} created. Status: {reservation.Status}. Total: {totalAmount:N2}.",
                    Type = "Reservation",
                    IsRead = false,
                    CreatedAt = ViaReservaERP.AppTime.Now
                });
            }

            await _db.SaveChangesAsync(ct);
            await tx.CommitAsync(ct);

            TempData["SuccessMessage"] = model.CheckInNow
                ? $"Walk-in created and checked in (#{reservation.ReservationId})."
                : $"Walk-in reservation created (#{reservation.ReservationId}).";

            return RedirectToAction(nameof(Reservations));
        }
        catch (Exception)
        {
            await tx.RollbackAsync(ct);
            ModelState.AddModelError(string.Empty, "An error occurred while creating the walk-in reservation.");
            await RebuildCreateListsAsync(model, companyId, ct);
            ViewData["Title"] = "Walk-in Reservation";
            return View(model);
        }
    }

    private async Task RebuildCreateListsAsync(WalkInReservationViewModel model, int companyId, CancellationToken ct)
    {
        model.Guests = await _db.Guests
            .Where(g => g.CompanyId == companyId && !g.IsDeleted)
            .OrderBy(g => g.FullName)
            .Select(g => new SelectListItem { Value = g.GuestId.ToString(), Text = g.FullName })
            .ToListAsync(ct);

        model.AvailableRooms = await _db.Rooms
            .Include(r => r.RoomType)
            .Where(r => r.CompanyId == companyId && r.Status == "Available" && !r.IsDeleted)
            .Select(r => new RoomSelectionItem
            {
                RoomId = r.RoomId,
                RoomNumber = r.RoomNumber != null ? r.RoomNumber : "N/A",
                TypeName = r.RoomType != null ? r.RoomType.TypeName : "Standard",
                BasePrice = r.RoomType != null && r.RoomType.BasePrice.HasValue ? r.RoomType.BasePrice.Value : 0m
            })
            .ToListAsync(ct);
    }

    public async Task<IActionResult> Guests(string? search, string sort = "created", string dir = "desc", int page = 1, int pageSize = 20, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;

        var baseQuery = _db.Guests
            .Where(g => g.CompanyId == companyId && !g.IsDeleted);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            baseQuery = baseQuery.Where(g => g.FullName.Contains(term) || g.Email.Contains(term));
        }

        var asc = !string.Equals(dir, "desc", StringComparison.OrdinalIgnoreCase);
        var q = sort?.ToLowerInvariant() switch
        {
            "id" => asc ? baseQuery.OrderBy(g => g.GuestId) : baseQuery.OrderByDescending(g => g.GuestId),
            "created" => asc
                ? baseQuery.OrderBy(g => g.CreatedAt).ThenBy(g => g.GuestId)
                : baseQuery.OrderByDescending(g => g.CreatedAt).ThenByDescending(g => g.GuestId),
            "email" => asc ? baseQuery.OrderBy(g => g.Email) : baseQuery.OrderByDescending(g => g.Email),
            _ => asc ? baseQuery.OrderBy(g => g.FullName) : baseQuery.OrderByDescending(g => g.FullName)
        };

        if (page <= 0) page = 1;
        if (pageSize <= 0) pageSize = 20;

        var totalRows = await q.CountAsync(ct);
        var rows = await q.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(ct);

        var now = ViaReservaERP.AppTime.Now;
        var firstOfMonth = new DateTime(now.Year, now.Month, 1);

        var model = new GuestsViewModel
        {
            Rows = rows,
            Search = search,
            Sort = sort,
            Dir = dir,
            Page = page,
            PageSize = pageSize,
            TotalRows = totalRows,
            TotalGuests = await baseQuery.CountAsync(ct),
            ActiveGuests = await baseQuery.CountAsync(g => g.IsActive, ct),
            InactiveGuests = await baseQuery.CountAsync(g => !g.IsActive, ct),
            NewGuestsThisMonth = await baseQuery.CountAsync(g => g.CreatedAt >= firstOfMonth, ct)
        };

        ViewData["Title"] = "Guest Management";
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateGuest(int id, string fullName, string? email, string? phone, bool? isActive, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;

        if (string.IsNullOrWhiteSpace(fullName))
        {
            TempData["ErrorMessage"] = "Full name is required.";
            return RedirectToAction(nameof(Guests));
        }

        var guest = await _db.Guests.FirstOrDefaultAsync(g => g.GuestId == id && g.CompanyId == companyId && !g.IsDeleted, ct);
        if (guest == null) return NotFound();

        guest.FullName = fullName.Trim();
        guest.Email = string.IsNullOrWhiteSpace(email) ? null : email.Trim();
        guest.Phone = string.IsNullOrWhiteSpace(phone) ? null : phone.Trim();
        guest.IsActive = isActive == true;

        _db.AuditLogs.Add(NewAudit(
            action: "Update",
            tableName: "Guests",
            recordId: guest.GuestId,
            newValues: $"Guest updated: {guest.FullName} ({guest.Email})"));

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Guest information updated.";
        return RedirectToAction(nameof(Guests));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckIn(int id, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var res = await _db.Reservations
            .Include(r => r.ReservationRooms)
                .ThenInclude(rr => rr.Room)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.ReservationId == id, ct);
        if (res == null) return NotFound();

        if (string.Equals(res.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) || string.Equals(res.Status, "Pending", StringComparison.OrdinalIgnoreCase))
        {
            res.Status = "Checked In";

            var roomAssignment = res.ReservationRooms.FirstOrDefault();
            var room = roomAssignment?.Room;
            if (room != null)
            {
                room.Status = "Occupied";
            }

            var unbilledCompletedServices = await _db.ServiceRequests
                .Include(sr => sr.Service)
                .Where(sr => sr.CompanyId == companyId && sr.GuestId == res.GuestId && sr.Status == "Completed" && sr.ReservationId == null)
                .ToListAsync(ct);

            foreach (var req in unbilledCompletedServices)
            {
                if (req.Service != null)
                {
                    _db.ReservationServices.Add(new ReservationService
                    {
                        ReservationId = res.ReservationId,
                        ServiceId = req.Service.ServiceId,
                        Quantity = 1,
                        Price = req.Service.Price
                    });

                    req.ReservationId = res.ReservationId;

                    var taxResult = _tax.CalculateTaxes(req.Service.Price ?? 0m, companyId);
                    res.TotalAmount = (res.TotalAmount ?? 0m) + taxResult.Total;
                    res.Subtotal = (res.Subtotal ?? 0m) + taxResult.Subtotal;
                    res.TaxAmount = (res.TaxAmount ?? 0m) + taxResult.TaxAmount;
                    res.ServiceCharge = (res.ServiceCharge ?? 0m) + taxResult.ServiceCharge;

                    // Record as a pending payment for Guest Portal
                    _db.Payments.Add(new Payment
                    {
                        CompanyId = companyId,
                        ReservationId = res.ReservationId,
                        Amount = taxResult.Total,
                        PaymentMethod = "Room Charge",
                        Status = "Succeeded",
                        StripePaymentIntentId = "Service Request #" + req.RequestId,
                        CreatedAt = ViaReservaERP.AppTime.Now
                    });

                    // Record in Accounting Portal as a transaction
                    _db.Transactions.Add(new AccountingTransaction
                    {
                        CompanyId = companyId,
                        Subtotal = taxResult.Subtotal,
                        TaxAmount = taxResult.TaxAmount,
                        ServiceCharge = taxResult.ServiceCharge,
                        Amount = taxResult.Total,
                        Type = "Income",
                        Description = $"Service Charge (Check-in): {req.Service.ServiceName} (Request #{req.RequestId}) for Res #{res.ReservationId}",
                        ReferenceId = req.RequestId,
                        ReferenceType = "ServiceRequest",
                        TransactionDate = ViaReservaERP.AppTime.Now
                    });
                }
            }

            _db.AuditLogs.Add(NewAudit(
                action: "Update",
                tableName: "Reservations",
                recordId: res.ReservationId,
                newValues: $"FrontDesk check-in: Reservation #{res.ReservationId} Room {room?.RoomNumber}"));

            await _db.SaveChangesAsync(ct);
            TempData["SuccessMessage"] = $"Reservation #{res.ReservationId} checked in.";
        }
        else
        {
            TempData["ErrorMessage"] = "Reservation cannot be checked in from its current status.";
        }

        return RedirectToAction(nameof(CheckInOut));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CheckOut(int id, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var res = await _db.Reservations
            .Include(r => r.ReservationRooms)
                .ThenInclude(rr => rr.Room)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.ReservationId == id, ct);
        if (res == null) return NotFound();

        if (string.Equals(res.Status, "Checked In", StringComparison.OrdinalIgnoreCase))
        {
            res.Status = "Checked Out";

            var roomAssignment = res.ReservationRooms.FirstOrDefault();
            var room = roomAssignment?.Room;
            if (room != null)
            {
                room.Status = "Available";
            }

            _db.AuditLogs.Add(NewAudit(
                action: "Update",
                tableName: "Reservations",
                recordId: res.ReservationId,
                newValues: $"FrontDesk check-out: Reservation #{res.ReservationId} Room {room?.RoomNumber}"));

            await _db.SaveChangesAsync(ct);
            TempData["SuccessMessage"] = $"Reservation #{res.ReservationId} checked out.";
        }
        else
        {
            TempData["ErrorMessage"] = "Reservation cannot be checked out from its current status.";
        }

        return RedirectToAction(nameof(CheckInOut));
    }

    public async Task<IActionResult> CheckInOut(DateOnly? date = null, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var d = date ?? DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);

        var arrivals = await _db.Reservations
            .Include(r => r.Guest)
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room)
            .Where(r => r.CompanyId == companyId)
            .Where(r => r.CheckInDate == d)
            .OrderByDescending(r => r.ReservationId)
            .ToListAsync(ct);

        var inHouse = await _db.Reservations
            .Include(r => r.Guest)
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room)
            .Where(r => r.CompanyId == companyId)
            .Where(r => r.Status == "Checked In")
            .OrderByDescending(r => r.ReservationId)
            .ToListAsync(ct);

        var departures = await _db.Reservations
            .Include(r => r.Guest)
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room)
            .Where(r => r.CompanyId == companyId)
            .Where(r => r.CheckOutDate == d)
            .OrderByDescending(r => r.ReservationId)
            .ToListAsync(ct);

        var model = new CheckInOutViewModel
        {
            Date = d,
            Arrivals = arrivals,
            InHouse = inHouse,
            Departures = departures
        };

        ViewData["Title"] = "Check-in/out";
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
                Description = $"FrontDesk Cancellation Refund #{res.ReservationId}",
                ReferenceId = res.ReservationId, ReferenceType = "Reservation",
                TransactionDate = ViaReservaERP.AppTime.Now
            });
        }

        _db.AuditLogs.Add(NewAudit("Reservation Cancelled", "Reservations", res.ReservationId,
            $"FrontDesk cancelled reservation #{res.ReservationId} for {res.Guest?.FullName}"));

        // Notify guest
        var guestUserId = res.Guest?.UserId;
        if (guestUserId.HasValue)
            _db.Notifications.Add(new Notification { UserId = guestUserId.Value, CompanyId = companyId,
                Title = "Reservation Cancelled", Message = $"Your reservation #{res.ReservationId} was cancelled by Front Desk. Refund: {(payment?.Status ?? "N/A")}.",
                Type = "Reservation", IsRead = false, CreatedAt = ViaReservaERP.AppTime.Now });
        await _db.SaveChangesAsync(ct);

        TempData["SuccessMessage"] = $"Reservation #{res.ReservationId} cancelled.";
        return RedirectToAction(nameof(Reservations));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ExtendStay(int id, DateOnly newCheckOutDate, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var res = await _db.Reservations
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room).ThenInclude(room => room!.RoomType)
            .Include(r => r.Guest)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.ReservationId == id, ct);
        if (res == null) return NotFound();

        if (res.Status != "Checked In")
        {
            TempData["ErrorMessage"] = "Can only extend a checked-in reservation.";
            return RedirectToAction(nameof(CheckInOut));
        }

        if (res.CheckOutDate == null || newCheckOutDate <= res.CheckOutDate.Value)
        {
            TempData["ErrorMessage"] = "New check-out date must be after the current check-out date.";
            return RedirectToAction(nameof(CheckInOut));
        }

        var oldCheckOut = res.CheckOutDate.Value;
        var additionalNights = newCheckOutDate.DayNumber - oldCheckOut.DayNumber;
        var roomPrice = res.ReservationRooms.FirstOrDefault()?.Room?.RoomType?.BasePrice ?? 0m;
        var taxResult = _tax.CalculateTaxes(roomPrice * additionalNights, companyId);

        res.CheckOutDate = newCheckOutDate;
        res.Subtotal = (res.Subtotal ?? 0m) + taxResult.Subtotal;
        res.TaxAmount = (res.TaxAmount ?? 0m) + taxResult.TaxAmount;
        res.ServiceCharge = (res.ServiceCharge ?? 0m) + taxResult.ServiceCharge;
        res.TotalAmount = (res.TotalAmount ?? 0m) + taxResult.Total;

        _db.Transactions.Add(new AccountingTransaction
        {
            CompanyId = companyId, Subtotal = taxResult.Subtotal, TaxAmount = taxResult.TaxAmount,
            ServiceCharge = taxResult.ServiceCharge, Amount = taxResult.Total, Type = "Income",
            Description = $"Stay Extension (+{additionalNights} nights) Res #{res.ReservationId}",
            ReferenceId = res.ReservationId, ReferenceType = "Reservation",
            TransactionDate = ViaReservaERP.AppTime.Now
        });

        _db.AuditLogs.Add(NewAudit("Stay Extended", "Reservations", res.ReservationId,
            $"FrontDesk extended checkout {oldCheckOut:yyyy-MM-dd} → {newCheckOutDate:yyyy-MM-dd} (+{additionalNights}n, +₱{taxResult.Total:N2})"));

        // Notify guest
        var guestUserId2 = res.Guest?.UserId;
        if (guestUserId2.HasValue)
            _db.Notifications.Add(new Notification { UserId = guestUserId2.Value, CompanyId = companyId,
                Title = "Stay Extended", Message = $"Your stay for reservation #{res.ReservationId} has been extended to {newCheckOutDate:MMM dd, yyyy}. Additional: ₱{taxResult.Total:N2}.",
                Type = "Reservation", IsRead = false, CreatedAt = ViaReservaERP.AppTime.Now });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = $"Reservation #{res.ReservationId} extended to {newCheckOutDate:MMM dd, yyyy}. +₱{taxResult.Total:N2}.";
        return RedirectToAction(nameof(CheckInOut));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EarlyCheckOut(int id, CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var res = await _db.Reservations
            .Include(r => r.ReservationRooms).ThenInclude(rr => rr.Room).ThenInclude(room => room!.RoomType)
            .Include(r => r.Payments)
            .Include(r => r.Guest)
            .FirstOrDefaultAsync(r => r.CompanyId == companyId && r.ReservationId == id, ct);
        if (res == null) return NotFound();

        if (res.Status != "Checked In")
        {
            TempData["ErrorMessage"] = "Can only early check-out a checked-in reservation.";
            return RedirectToAction(nameof(CheckInOut));
        }

        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
        if (res.CheckOutDate == null || res.CheckOutDate.Value.DayNumber - today.DayNumber <= 0)
        {
            TempData["ErrorMessage"] = "No days remain for early check-out.";
            return RedirectToAction(nameof(CheckInOut));
        }

        var reducedNights = res.CheckOutDate.Value.DayNumber - today.DayNumber;
        var roomPrice = res.ReservationRooms.FirstOrDefault()?.Room?.RoomType?.BasePrice ?? 0m;
        var refundTax = _tax.CalculateTaxes(roomPrice * reducedNights, companyId);

        res.CheckOutDate = today;
        res.Status = "Checked Out";
        res.Subtotal = Math.Max(0m, (res.Subtotal ?? 0m) - refundTax.Subtotal);
        res.TaxAmount = Math.Max(0m, (res.TaxAmount ?? 0m) - refundTax.TaxAmount);
        res.ServiceCharge = Math.Max(0m, (res.ServiceCharge ?? 0m) - refundTax.ServiceCharge);
        res.TotalAmount = Math.Max(0m, (res.TotalAmount ?? 0m) - refundTax.Total);

        foreach (var rr in res.ReservationRooms)
            if (rr.Room != null) rr.Room.Status = "Available";

        if (refundTax.Total > 0m)
        {
            _db.Transactions.Add(new AccountingTransaction
            {
                CompanyId = companyId, Subtotal = -refundTax.Subtotal, TaxAmount = -refundTax.TaxAmount,
                ServiceCharge = -refundTax.ServiceCharge, Amount = -refundTax.Total, Type = "Refund",
                Description = $"Early Check-out Refund ({reducedNights} nights) Res #{res.ReservationId}",
                ReferenceId = res.ReservationId, ReferenceType = "Reservation",
                TransactionDate = ViaReservaERP.AppTime.Now
            });
        }

        _db.AuditLogs.Add(NewAudit("Early Check-out", "Reservations", res.ReservationId,
            $"FrontDesk early checkout. {reducedNights} unused nights refunded ₱{refundTax.Total:N2}"));

        var guestUserId3 = res.Guest?.UserId;
        if (guestUserId3.HasValue)
            _db.Notifications.Add(new Notification { UserId = guestUserId3.Value, CompanyId = companyId,
                Title = "Early Check-out", Message = $"You have been checked out early from reservation #{res.ReservationId}. Refund: ₱{refundTax.Total:N2}.",
                Type = "Reservation", IsRead = false, CreatedAt = ViaReservaERP.AppTime.Now });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = $"Reservation #{res.ReservationId} checked out early. Refund: ₱{refundTax.Total:N2}.";
        return RedirectToAction(nameof(CheckInOut));
    }

    public async Task<IActionResult> Profile(CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId() ?? 0;

        var user = await _db.Users
            .Include(u => u.Company)
            .FirstOrDefaultAsync(u => u.UserId == userId && u.CompanyId == companyId, ct);

        if (user == null) return NotFound();

        ViewData["Title"] = "Profile Settings";
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
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId() ?? 0;

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
            IPAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            UserAgent = Request.Headers["User-Agent"].ToString(),
            ActionDate = ViaReservaERP.AppTime.Now
        });

        await _db.SaveChangesAsync(ct);
        TempData["SuccessMessage"] = "Profile updated.";
        return RedirectToAction(nameof(Profile));
    }

    public async Task<IActionResult> Notifications(CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId() ?? 0;

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
        var userId = User.GetUserId() ?? 0;

        var unread = await _db.Notifications
            .AsNoTracking()
            .CountAsync(n => n.CompanyId == companyId && n.UserId == userId && !n.IsRead, ct);

        return Json(new { unread });
    }

    [HttpGet]
    public async Task<IActionResult> NotificationsListPartial(CancellationToken ct = default)
    {
        var companyId = CurrentCompanyId;
        var userId = User.GetUserId() ?? 0;

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
        var userId = User.GetUserId() ?? 0;

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
