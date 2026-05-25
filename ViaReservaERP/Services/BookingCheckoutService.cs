using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ViaReservaERP.Data;
using ViaReservaERP.Models;
using ViaReservaERP.Models.Reservations;
using ViaReservaERP.Security;

namespace ViaReservaERP.Services;

public class BookingCheckoutService : IBookingCheckoutService
{
    private readonly ViaReservaDbContext _db;
    private readonly IStripePaymentService _stripe;
    private readonly INotificationService _notify;
    private readonly ITaxService _tax;

    public BookingCheckoutService(ViaReservaDbContext db, IStripePaymentService stripe, INotificationService notify, ITaxService tax)
    {
        _db = db;
        _stripe = stripe;
        _notify = notify;
        _tax = tax;
    }

    public async Task<int> FinalizeReservationBookingAsync(BookReservationViewModel model, int? authenticatedUserId = null, CancellationToken ct = default)
    {
        if (model.CheckOutDate <= model.CheckInDate)
            throw new InvalidOperationException("Check-out must be after check-in.");

        var normalizedEmail = (model.Email ?? string.Empty).Trim().ToLowerInvariant();
        var normalizedFullName = (model.FullName ?? string.Empty).Trim();
        var normalizedPhone = string.IsNullOrWhiteSpace(model.Phone) ? null : model.Phone.Trim();
        var normalizedPassword = string.IsNullOrWhiteSpace(model.Password) ? null : model.Password.Trim();

        var paymentReference = model.StripePaymentIntentId;
        var paymentMethod = "Stripe";

        if (model.DemoPayment)
        {
            paymentMethod = "Demo";
            paymentReference = string.IsNullOrWhiteSpace(paymentReference)
                ? $"DEMO-{Guid.NewGuid():N}"
                : paymentReference;
        }
        else
        {
            if (string.IsNullOrWhiteSpace(model.StripePaymentIntentId))
                throw new InvalidOperationException("Missing payment confirmation.");

            var piStatus = await _stripe.GetPaymentIntentStatusAsync(model.StripePaymentIntentId, ct);
            if (!string.Equals(piStatus, "succeeded", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Payment not successful (status: {piStatus ?? "unknown"}).");
        }

        var company = await _db.Companies
            .FirstOrDefaultAsync(c => c.CompanyId == model.CompanyId && !c.IsDeleted && c.IsActive, ct);
        if (company is null)
            throw new InvalidOperationException("Selected hotel is not available.");

        var room = await _db.Rooms
            .FirstOrDefaultAsync(r => r.RoomId == model.SelectedRoomId && r.CompanyId == model.CompanyId && r.Status == "Available", ct);
        if (room is null)
            throw new InvalidOperationException("Selected room is no longer available. Please choose another room.");

        var nights = Math.Max(0, model.CheckOutDate.DayNumber - model.CheckInDate.DayNumber);
        if (nights <= 0)
            throw new InvalidOperationException("Invalid date range.");

        List<SelectedServiceDto> selectedServices;
        try
        {
            var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            selectedServices = JsonSerializer.Deserialize<List<SelectedServiceDto>>(model.SelectedServicesJson ?? "[]", opts) ?? new();
        }
        catch
        {
            selectedServices = new();
        }

        var selectedServiceIds = selectedServices.Select(s => s.ServiceId).Distinct().ToList();
        var serviceMap = await _db.Services
            .AsNoTracking()
            .Where(s => s.CompanyId.HasValue && s.CompanyId.Value == model.CompanyId)
            .Where(s => selectedServiceIds.Contains(s.ServiceId))
            .Select(s => new { s.ServiceId, Price = s.Price ?? 0m })
            .ToDictionaryAsync(x => x.ServiceId, x => x.Price, ct);

        var servicesTotal = 0m;
        foreach (var svc in selectedServices)
        {
            if (!serviceMap.TryGetValue(svc.ServiceId, out var actualPrice))
                continue;
            var qty = Math.Max(1, svc.Quantity);
            servicesTotal += actualPrice * qty;
        }

        var roomTotal = model.SelectedRoomTypePrice * nights;
        var taxResult = _tax.CalculateTaxes(roomTotal + servicesTotal, model.CompanyId);
        var grandTotal = taxResult.Total;

        var nowUtc = ViaReservaERP.AppTime.Now;

        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        try
        {
            var wantsAccount = !string.IsNullOrWhiteSpace(normalizedPassword);

            ErpUser? user = null;

            if (authenticatedUserId.HasValue)
            {
                user = await _db.Users.FirstOrDefaultAsync(u =>
                    u.UserId == authenticatedUserId.Value &&
                    !u.IsDeleted &&
                    u.IsActive, ct);

                if (user is null)
                    throw new InvalidOperationException("Authenticated user not found.");

                if (user.CompanyId != model.CompanyId)
                    user.CompanyId = model.CompanyId;

                if (!string.Equals(user.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("Booking email must match your logged-in account.");
            }
            else if (wantsAccount)
            {
                var existingUser = await _db.Users
                    .FirstOrDefaultAsync(u => u.Email == normalizedEmail, ct);

                if (existingUser == null)
                {
                    user = new ErpUser
                    {
                        CompanyId = model.CompanyId,
                        RoleId = 6,
                        FullName = normalizedFullName,
                        Email = normalizedEmail,
                        PasswordHash = PasswordHasher.Hash(normalizedPassword!),
                        IsActive = true,
                        IsDeleted = false,
                        CreatedAt = nowUtc
                    };

                    _db.Users.Add(user);
                    await _db.SaveChangesAsync(ct);

                    _db.AuditLogs.Add(new AuditLog
                    {
                        CompanyId = model.CompanyId,
                        UserId = user.UserId,
                        Action = "Guest Registration",
                        TableName = "Users",
                        RecordId = user.UserId,
                        ActionDate = nowUtc
                    });
                }
                else
                {
                    if (existingUser.IsDeleted || !existingUser.IsActive)
                        throw new InvalidOperationException("Account is disabled.");

                    if (existingUser.CompanyId != model.CompanyId)
                        existingUser.CompanyId = model.CompanyId;

                    if (!PasswordHasher.Verify(normalizedPassword!, existingUser.PasswordHash))
                        throw new InvalidOperationException("Incorrect password for existing account.");

                    user = existingUser;
                }
            }
            else
            {
                throw new InvalidOperationException("Please set a password to create your guest portal account, or log in first.");
            }

            var guest = await _db.Guests
                .FirstOrDefaultAsync(g => g.UserId == user!.UserId, ct);

            if (guest is null)
            {
                guest = new Guest
                {
                    CompanyId = model.CompanyId,
                    UserId = user!.UserId,
                    FullName = normalizedFullName,
                    Email = normalizedEmail,
                    Phone = normalizedPhone
                };

                _db.Guests.Add(guest);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                guest.CompanyId = model.CompanyId;
                guest.FullName = normalizedFullName;
                guest.Email = normalizedEmail;
                guest.Phone = normalizedPhone;
            }

            var reservation = new Reservation
            {
                CompanyId = model.CompanyId,
                GuestId = guest.GuestId,
                CheckInDate = model.CheckInDate,
                CheckOutDate = model.CheckOutDate,
                Status = "Confirmed",
                Subtotal = taxResult.Subtotal,
                TaxAmount = taxResult.TaxAmount,
                ServiceCharge = taxResult.ServiceCharge,
                TotalAmount = taxResult.Total,
                CreatedAt = nowUtc
            };

            _db.Reservations.Add(reservation);
            await _db.SaveChangesAsync(ct);

            _db.ReservationRooms.Add(new ReservationRoom
            {
                ReservationId = reservation.ReservationId,
                RoomId = room.RoomId,
                Price = model.SelectedRoomTypePrice
            });

            room.Status = "Reserved";

            foreach (var svc in selectedServices)
            {
                if (!serviceMap.TryGetValue(svc.ServiceId, out var actualPrice))
                    continue;
                var qty = Math.Max(1, svc.Quantity);
                _db.ReservationServices.Add(new ReservationService
                {
                    ReservationId = reservation.ReservationId,
                    ServiceId = svc.ServiceId,
                    Quantity = qty,
                    Price = actualPrice
                });

                _db.ServiceRequests.Add(new ServiceRequest
                {
                    CompanyId = model.CompanyId,
                    GuestId = guest.GuestId,
                    ReservationId = reservation.ReservationId,
                    ServiceId = svc.ServiceId,
                    Status = "Pending",
                    RequestDate = nowUtc
                });
            }

            var payment = new Payment
            {
                CompanyId = model.CompanyId,
                ReservationId = reservation.ReservationId,
                Amount = grandTotal,
                PaymentMethod = paymentMethod,
                Status = "Succeeded",
                StripePaymentIntentId = paymentReference,
                CreatedAt = nowUtc
            };

            _db.Payments.Add(payment);
            await _db.SaveChangesAsync(ct);

            _db.Transactions.Add(new AccountingTransaction
            {
                CompanyId = model.CompanyId,
                Subtotal = taxResult.Subtotal,
                TaxAmount = taxResult.TaxAmount,
                ServiceCharge = taxResult.ServiceCharge,
                Amount = taxResult.Total,
                Type = "Income",
                Description = $"Reservation Payment for Res #{reservation.ReservationId} via {paymentMethod}",
                ReferenceId = reservation.ReservationId,
                ReferenceType = "Reservation",
                TransactionDate = nowUtc
            });

            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = model.CompanyId,
                UserId = user?.UserId,
                Action = "Insert",
                TableName = "Transactions",
                RecordId = reservation.ReservationId,
                NewValues = $"Income recorded for Res #{reservation.ReservationId}: {grandTotal:N2} via {paymentMethod}",
                ActionDate = nowUtc
            });

            var workflowId = await _db.Workflows
                .AsNoTracking()
                .Where(w => w.CompanyId == model.CompanyId)
                .OrderByDescending(w => (w.Name ?? "").ToLower().Contains("booking"))
                .ThenBy(w => w.WorkflowId)
                .Select(w => (int?)w.WorkflowId)
                .FirstOrDefaultAsync(ct);

            var instance = new WorkflowInstance
            {
                WorkflowId = workflowId,
                CompanyId = model.CompanyId,
                ReferenceId = reservation.ReservationId,
                ReferenceType = "Reservation",
                CurrentStep = 1,
                Status = "InProgress",
                CreatedAt = nowUtc
            };
            _db.WorkflowInstances.Add(instance);
            await _db.SaveChangesAsync(ct);

            var stages = new[] { "Booking Created", "Payment Verified", "Reservation Approved", "Room Assignment", "Service Assignment", "Check-In", "Check-Out", "Completed" };

            foreach (var stage in stages)
            {
                _db.WorkflowInstanceSteps.Add(new WorkflowInstanceStep
                {
                    InstanceId = instance.InstanceId,
                    StepId = null,
                    ActionTaken = stage,
                    PerformedBy = user?.UserId,
                    PerformedAt = nowUtc
                });
            }

            _db.AuditLogs.Add(new AuditLog
            {
                CompanyId = model.CompanyId,
                UserId = user?.UserId,
                Action = "Reservation Created",
                TableName = "Reservations",
                RecordId = reservation.ReservationId,
                ActionDate = nowUtc
            });

            await _db.SaveChangesAsync(ct);

            var companyName = company.CompanyName;
            var guestEmail = guest.Email ?? string.Empty;
            var guestUserId = user?.UserId;

            if (guestUserId.HasValue)
            {
                await _notify.NotifyUserAsync(guestUserId.Value,
                    "Booking Confirmed",
                    $"Reservation #{reservation.ReservationId} confirmed. Total: {grandTotal:N2}.",
                    "Reservation",
                    model.CompanyId,
                    ct);
            }

            await _notify.NotifyRoleAsync(model.CompanyId, roleId: 2,
                title: "New Booking",
                message: $"New reservation #{reservation.ReservationId} created by {(user?.FullName ?? guest.FullName ?? "Guest")}.",
                type: "Reservation",
                ct);

            await tx.CommitAsync(ct);

            var resultReservationId = reservation.ReservationId;

            try
            {
                var subjectBooking = $"ViaReserva: Booking Confirmed #{reservation.ReservationId}";
                var bookingText = $"Reservation #{reservation.ReservationId} confirmed at {companyName}.\nTotal Paid: {grandTotal:N2}.";
                if (!string.IsNullOrWhiteSpace(guestEmail))
                    await _notify.EmailUserAsync(guestEmail, subjectBooking, bookingText, html: null, ct);
            }
            catch { }

            return resultReservationId;
        }
        catch
        {
            await tx.RollbackAsync(ct);
            throw;
        }
    }

    public async Task SyncRoomStatusesAsync(int? companyId = null, CancellationToken ct = default)
    {
        var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);

        // Find rooms that are 'Occupied' or 'Reserved' but no longer have an active/current reservation today.
        var query = _db.Rooms.Where(r => !r.IsDeleted && (r.Status == "Occupied" || r.Status == "Reserved"));
        if (companyId.HasValue)
        {
            query = query.Where(r => r.CompanyId == companyId.Value);
        }

        var roomsToCheck = await query.ToListAsync(ct);
        if (!roomsToCheck.Any()) return;

        var roomIds = roomsToCheck.Select(r => r.RoomId).ToList();

        // A room should stay 'Occupied' if it has a 'Checked In' reservation covering today.
        // A room should stay 'Reserved' if it has a 'Confirmed' reservation starting today.
        var currentReservations = await _db.ReservationRooms
            .Include(rr => rr.Reservation)
            .Where(rr => rr.RoomId.HasValue && roomIds.Contains(rr.RoomId.Value))
            .Where(rr => rr.Reservation != null && (rr.Reservation.Status == "Checked In" || rr.Reservation.Status == "Confirmed"))
            .Where(rr => rr.Reservation.CheckInDate <= today && rr.Reservation.CheckOutDate >= today)
            .ToListAsync(ct);

        bool changed = false;
        foreach (var room in roomsToCheck)
        {
            var hasCurrent = currentReservations.Any(cr => cr.RoomId == room.RoomId);
            if (!hasCurrent)
            {
                room.Status = "Available";
                changed = true;
            }
        }

        if (changed)
        {
            await _db.SaveChangesAsync(ct);
        }
    }
}
