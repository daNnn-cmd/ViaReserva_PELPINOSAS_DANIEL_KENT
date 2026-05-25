using Microsoft.EntityFrameworkCore;
using ViaReservaERP.Data;
using ViaReservaERP.Models;

namespace ViaReservaERP.Services;

public class ServiceRequestAppService : IServiceRequestAppService
{
    private readonly ViaReservaDbContext _db;
    private readonly INotificationService _notify;
    private readonly ITaxService _tax;

    public ServiceRequestAppService(ViaReservaDbContext db, INotificationService notify, ITaxService tax)
    {
        _db = db;
        _notify = notify;
        _tax = tax;
    }

    public async Task AssignAsync(int requestId, int assignedToUserId, int performedByUserId, CancellationToken ct = default)
    {
        var req = await _db.ServiceRequests
            .Include(r => r.Guest)
            .ThenInclude(g => g!.User)
            .Include(r => r.Service)
            .FirstOrDefaultAsync(r => r.RequestId == requestId, ct);

        if (req == null)
            throw new InvalidOperationException("Service request not found.");

        var oldAssignedTo = req.AssignedTo;
        req.AssignedTo = assignedToUserId;

        var oldStatus = req.Status ?? "";
        if (!string.Equals(oldStatus, "Assigned", StringComparison.OrdinalIgnoreCase))
            req.Status = "Assigned";

        await _db.SaveChangesAsync(ct);

        await CreateAuditAsync(req.CompanyId, performedByUserId,
            action: "Service Request Assigned",
            tableName: "ServiceRequests",
            recordId: req.RequestId,
            newValues: $"AssignedTo={assignedToUserId};Status={req.Status}",
            ct);

        await NotifyGuestStatusChangeAsync(req, oldStatus, req.Status ?? "", ct);

        if (oldAssignedTo != assignedToUserId)
        {
            await _notify.NotifyUserAsync(assignedToUserId,
                "Service Request Assigned",
                $"You have been assigned to service request #{req.RequestId}.",
                "Service",
                req.CompanyId,
                ct);
        }
    }

    public async Task UpdateStatusAsync(int requestId, string newStatus, int performedByUserId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(newStatus))
            throw new InvalidOperationException("Status is required.");

        var req = await _db.ServiceRequests
            .Include(r => r.Guest)
            .ThenInclude(g => g!.User)
            .Include(r => r.Service)
            .FirstOrDefaultAsync(r => r.RequestId == requestId, ct);

        if (req == null)
            throw new InvalidOperationException("Service request not found.");

        var oldStatus = req.Status ?? "";
        if (string.Equals(oldStatus, newStatus, StringComparison.OrdinalIgnoreCase))
            return;

        req.Status = newStatus;

        if (string.Equals(newStatus, "Completed", StringComparison.OrdinalIgnoreCase))
        {
            if (!req.ReservationId.HasValue)
            {
                var today = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now);
                var activeRes = await _db.Reservations
                    .Include(r => r.ReservationServices)
                    .Where(r => r.CompanyId == req.CompanyId && r.GuestId == req.GuestId)
                    .Where(r => r.Status != "Cancelled" && r.Status != "Completed")
                    .Where(r => r.CheckInDate <= today && r.CheckOutDate >= today)
                    .OrderByDescending(r => r.ReservationId)
                    .FirstOrDefaultAsync(ct);

                if (activeRes != null && req.ServiceId.HasValue)
                {
                    var svc = await _db.Services.AsNoTracking().FirstOrDefaultAsync(s => s.ServiceId == req.ServiceId.Value, ct);
                    if (svc != null)
                    {
                        _db.ReservationServices.Add(new ReservationService
                        {
                            ReservationId = activeRes.ReservationId,
                            ServiceId = svc.ServiceId,
                            Quantity = 1,
                            Price = svc.Price
                        });

                        var taxResult = _tax.CalculateTaxes(svc.Price ?? 0m, req.CompanyId ?? 0);
                        activeRes.TotalAmount = (activeRes.TotalAmount ?? 0m) + taxResult.Total;
                        activeRes.Subtotal = (activeRes.Subtotal ?? 0m) + taxResult.Subtotal;
                        activeRes.TaxAmount = (activeRes.TaxAmount ?? 0m) + taxResult.TaxAmount;
                        activeRes.ServiceCharge = (activeRes.ServiceCharge ?? 0m) + taxResult.ServiceCharge;

                        req.ReservationId = activeRes.ReservationId;

                        var servicePayment = new Payment
                        {
                            CompanyId = req.CompanyId,
                            ReservationId = activeRes.ReservationId,
                            Amount = taxResult.Total,
                            PaymentMethod = "Room Charge",
                            Status = "Succeeded",
                            StripePaymentIntentId = "Service Request #" + req.RequestId,
                            CreatedAt = ViaReservaERP.AppTime.Now
                        };
                        _db.Payments.Add(servicePayment);

                        _db.Transactions.Add(new AccountingTransaction
                        {
                            CompanyId = req.CompanyId ?? 0,
                            Subtotal = taxResult.Subtotal,
                            TaxAmount = taxResult.TaxAmount,
                            ServiceCharge = taxResult.ServiceCharge,
                            Amount = taxResult.Total,
                            Type = "Income",
                            Description = $"Service Charge: {svc.ServiceName} (Request #{req.RequestId}) for Res #{activeRes.ReservationId}",
                            ReferenceId = req.RequestId,
                            ReferenceType = "ServiceRequest",
                            TransactionDate = ViaReservaERP.AppTime.Now
                        });
                    }
                }
            }
        }

        await _db.SaveChangesAsync(ct);

        await CreateAuditAsync(req.CompanyId, performedByUserId,
            action: "Service Request Status Updated",
            tableName: "ServiceRequests",
            recordId: req.RequestId,
            newValues: $"{oldStatus} -> {newStatus}",
            ct);

        await NotifyGuestStatusChangeAsync(req, oldStatus, newStatus, ct);
    }

    private async Task NotifyGuestStatusChangeAsync(ServiceRequest req, string oldStatus, string newStatus, CancellationToken ct)
    {
        var guestUserId = req.Guest?.UserId;
        var guestEmail = req.Guest?.Email;
        var serviceName = req.Service?.ServiceName ?? "Service";

        if (guestUserId.HasValue)
        {
            await _notify.NotifyUserAsync(guestUserId.Value,
                "Service Request Update",
                $"Request #{req.RequestId} ({serviceName}) status changed: {oldStatus} → {newStatus}.",
                "Service",
                req.CompanyId,
                ct);
        }

        if (!string.IsNullOrWhiteSpace(guestEmail))
        {
            await _notify.EmailUserAsync(guestEmail, $"ViaReserva: Service Request #{req.RequestId} Update", $"Your request #{req.RequestId} for {serviceName} is now '{newStatus}'.", html: null, ct);
        }
    }

    private async Task CreateAuditAsync(int? companyId, int performedByUserId, string action, string tableName, int recordId, string? newValues, CancellationToken ct)
    {
        _db.AuditLogs.Add(new AuditLog
        {
            CompanyId = companyId,
            UserId = performedByUserId,
            Action = action,
            TableName = tableName,
            RecordId = recordId,
            NewValues = newValues,
            ActionDate = DateTime.SpecifyKind(ViaReservaERP.AppTime.Now, DateTimeKind.Utc)
        });

        await _db.SaveChangesAsync(ct);
    }
}
