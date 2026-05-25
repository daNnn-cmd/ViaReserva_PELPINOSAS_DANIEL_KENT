using Microsoft.EntityFrameworkCore;
using ViaReservaERP.Data;
using ViaReservaERP.Models.SuperAdmin;

namespace ViaReservaERP.Services;

public class SystemValidationService : ISystemValidationService
{
    private readonly ViaReservaDbContext _db;
    private readonly INotificationService _notify;

    public SystemValidationService(ViaReservaDbContext db, INotificationService notify)
    {
        _db = db;
        _notify = notify;
    }

    public Task<SystemValidationViewModel> RunAsync(CancellationToken ct = default)
    {
        return RunInternalAsync(escalate: false, performedByUserId: null, ct);
    }

    public Task<SystemValidationViewModel> RunAndEscalateAsync(int performedByUserId, CancellationToken ct = default)
    {
        return RunInternalAsync(escalate: true, performedByUserId, ct);
    }

    private async Task<SystemValidationViewModel> RunInternalAsync(bool escalate, int? performedByUserId, CancellationToken ct)
    {
        var runAt = ViaReservaERP.AppTime.Now;

        var orphanReservations = await _db.Reservations
            .AsNoTracking()
            .GroupJoin(_db.Guests.AsNoTracking(), r => r.GuestId, g => g.GuestId, (r, g) => new { r, g })
            .SelectMany(x => x.g.DefaultIfEmpty(), (x, g) => new { x.r, guest = g })
            .Where(x => x.guest == null)
            .Select(x => new { x.r.ReservationId, x.r.CompanyId })
            .ToListAsync(ct);

        var orphanReservationRooms = await _db.Reservations
            .AsNoTracking()
            .GroupJoin(_db.ReservationRooms.AsNoTracking(), r => r.ReservationId, rr => rr.ReservationId ?? 0, (r, rr) => new { r, rr })
            .SelectMany(x => x.rr.DefaultIfEmpty(), (x, rr) => new { x.r, rr })
            .Where(x => x.rr == null)
            .Select(x => new { x.r.ReservationId, x.r.CompanyId })
            .ToListAsync(ct);

        var orphanPayments = await _db.Payments
            .AsNoTracking()
            .Where(p => p.ReservationId == null || p.CompanyId == null)
            .Select(p => new { p.PaymentId, p.CompanyId, p.ReservationId, p.Status })
            .ToListAsync(ct);

        var orphanTransactions = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.CompanyId == null)
            .Select(t => new { t.TransactionId, t.CompanyId, t.ReferenceType, t.ReferenceId, t.Type, t.Amount })
            .ToListAsync(ct);

        var duplicateRevenue = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.Type != null && t.Type.ToLower() == "revenue")
            .Where(t => t.ReferenceType != null && t.ReferenceType.ToLower() == "reservation")
            .Where(t => t.ReferenceId != null)
            .GroupBy(t => new { t.CompanyId, t.ReferenceId })
            .Where(g => g.Count() > 1)
            .SelectMany(g => g.Select(t => new { t.TransactionId, t.CompanyId, t.ReferenceId, t.Amount }))
            .ToListAsync(ct);

        var wfOrphans = await _db.WorkflowInstances
            .AsNoTracking()
            .Where(w => w.ReferenceType == "Reservation")
            .GroupJoin(_db.Reservations.AsNoTracking(), w => w.ReferenceId, r => (int?)r.ReservationId, (w, r) => new { w, r })
            .SelectMany(x => x.r.DefaultIfEmpty(), (x, r) => new { x.w, reservation = r })
            .Where(x => x.w.ReferenceId != null && x.reservation == null)
            .Select(x => new { x.w.InstanceId, x.w.CompanyId, x.w.ReferenceId })
            .ToListAsync(ct);

        var incompleteWf = await _db.WorkflowInstances
            .AsNoTracking()
            .GroupJoin(_db.WorkflowInstanceSteps.AsNoTracking(), w => w.InstanceId, s => s.InstanceId, (w, s) => new { w, steps = s })
            .SelectMany(x => x.steps.DefaultIfEmpty(), (x, s) => new { x.w, step = s })
            .GroupBy(x => x.w.InstanceId)
            .Where(g => g.All(x => x.step == null))
            .Select(g => g.Key)
            .ToListAsync(ct);

        var orphanServiceRequests = await _db.ServiceRequests
            .AsNoTracking()
            .Where(sr => sr.GuestId == null || sr.CompanyId == null || sr.ServiceId == null)
            .Select(sr => new { sr.RequestId, sr.CompanyId, sr.GuestId, sr.ServiceId, sr.Status })
            .ToListAsync(ct);

        var failedPayments = await _db.Payments
            .AsNoTracking()
            .Where(p => p.Status != null && p.Status.ToLower().Contains("fail"))
            .Select(p => new { p.PaymentId, p.CompanyId, p.ReservationId, p.Amount, p.StripePaymentIntentId, p.Status })
            .ToListAsync(ct);

        var pendingRefunds = await _db.Payments
            .AsNoTracking()
            .Where(p => p.Status != null && p.Status.ToLower().Contains("refund") && p.Status.ToLower().Contains("pending"))
            .Select(p => new { p.PaymentId, p.CompanyId, p.ReservationId, p.Amount, p.StripePaymentIntentId, p.Status })
            .ToListAsync(ct);

        var revenuePaymentsSucceeded = await _db.Payments
            .AsNoTracking()
            .Where(p => p.Status != null && p.Status.ToLower().Contains("succeed"))
            .SumAsync(p => (decimal?)p.Amount ?? 0m, ct);

        var revenueTransactionsRevenue = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.Type != null && t.Type.ToLower() == "revenue")
            .SumAsync(t => (decimal?)t.Amount ?? 0m, ct);

        var revenueTxnByReservation = await _db.Transactions
            .AsNoTracking()
            .Where(t => t.Type != null && t.Type.ToLower() == "revenue")
            .Where(t => t.ReferenceType != null && t.ReferenceType.ToLower() == "reservation")
            .Where(t => t.ReferenceId != null)
            .GroupBy(t => new { t.CompanyId, ReservationId = t.ReferenceId!.Value })
            .Select(g => new
            {
                g.Key.CompanyId,
                g.Key.ReservationId,
                TransactionIds = g.Select(x => x.TransactionId).ToList(),
                TotalAmount = g.Sum(x => x.Amount ?? 0m)
            })
            .ToListAsync(ct);

        var succeededPayments = await _db.Payments
            .AsNoTracking()
            .Where(p => p.Status != null && p.Status.ToLower().Contains("succeed"))
            .Where(p => p.ReservationId != null)
            .Select(p => new { p.PaymentId, p.CompanyId, ReservationId = p.ReservationId!.Value, Amount = p.Amount ?? 0m, Status = p.Status ?? "", Ref = p.StripePaymentIntentId ?? "" })
            .ToListAsync(ct);

        var paymentKeys = succeededPayments
            .Select(p => new { p.CompanyId, p.ReservationId })
            .Distinct()
            .ToList();

        var txnKeys = revenueTxnByReservation
            .Select(t => new { t.CompanyId, t.ReservationId })
            .Distinct()
            .ToList();

        var paymentsWithoutTx = succeededPayments
            .Where(p => !txnKeys.Any(t => t.CompanyId == p.CompanyId && t.ReservationId == p.ReservationId))
            .ToList();

        var txWithoutPayments = revenueTxnByReservation
            .Where(t => !paymentKeys.Any(p => p.CompanyId == t.CompanyId && p.ReservationId == t.ReservationId))
            .ToList();

        var confirmedNoPay = await _db.Reservations
            .AsNoTracking()
            .Include(r => r.Guest)
            .Where(r => r.Status != null && r.Status.ToLower().Contains("confirm"))
            .Select(r => new { r.ReservationId, r.CompanyId, Guest = r.Guest != null ? (r.Guest.FullName ?? "") : "", Amount = r.TotalAmount ?? 0m, Status = r.Status ?? "" })
            .ToListAsync(ct);

        var confirmedWithoutSucceededPayment = confirmedNoPay
            .Where(r => !paymentKeys.Any(p => p.CompanyId == r.CompanyId && p.ReservationId == r.ReservationId))
            .ToList();

        var companies = await _db.Companies.AsNoTracking().ToDictionaryAsync(c => c.CompanyId, c => c.CompanyName, ct);

        var vm = new SystemValidationViewModel
        {
            RunAtUtc = runAt,
            OrphanReservations = orphanReservations.Count + orphanReservationRooms.Count,
            OrphanPayments = orphanPayments.Count,
            OrphanTransactions = orphanTransactions.Count,
            DuplicateRevenueTransactions = duplicateRevenue.Count,
            OrphanWorkflows = wfOrphans.Count,
            IncompleteWorkflowSteps = incompleteWf.Count,
            OrphanServiceRequests = orphanServiceRequests.Count,
            FailedPayments = failedPayments.Count,
            PendingRefunds = pendingRefunds.Count,
            RevenuePaymentsSucceeded = revenuePaymentsSucceeded,
            RevenueTransactionsRevenue = revenueTransactionsRevenue,
            RevenueDelta = revenuePaymentsSucceeded - revenueTransactionsRevenue,
            PaymentsWithoutTransactions = paymentsWithoutTx.Count,
            TransactionsWithoutPayments = txWithoutPayments.Count,
            ConfirmedReservationsWithoutSuccessfulPayment = confirmedWithoutSucceededPayment.Count
        };

        vm.OrphanReservationRows = orphanReservations.Select(x => new ValidationRow
        {
            Entity = "Reservation",
            CompanyId = x.CompanyId,
            Company = companies.TryGetValue(x.CompanyId, out var name) ? name : "",
            RecordId = x.ReservationId,
            Issue = "Missing guest",
            Ref = ""
        }).Concat(orphanReservationRooms.Select(x => new ValidationRow
        {
            Entity = "Reservation",
            CompanyId = x.CompanyId,
            Company = companies.TryGetValue(x.CompanyId, out var name) ? name : "",
            RecordId = x.ReservationId,
            Issue = "Missing room assignment",
            Ref = ""
        })).Take(250).ToList();

        vm.OrphanPaymentRows = orphanPayments.Take(250).Select(x => new ValidationRow
        {
            Entity = "Payment",
            CompanyId = x.CompanyId,
            Company = x.CompanyId.HasValue && companies.TryGetValue(x.CompanyId.Value, out var name) ? name : "",
            RecordId = x.PaymentId,
            Issue = "Missing reservation/company",
            Ref = x.ReservationId.HasValue ? $"Reservation #{x.ReservationId.Value}" : ""
        }).ToList();

        vm.PaymentsWithoutTransactionRows = paymentsWithoutTx.Take(250).Select(x => new FinancialMismatchRow
        {
            CompanyId = x.CompanyId,
            Company = x.CompanyId.HasValue && companies.TryGetValue(x.CompanyId.Value, out var name) ? name : "",
            PaymentId = x.PaymentId,
            TransactionId = null,
            ReservationId = x.ReservationId,
            Amount = x.Amount,
            Status = x.Status,
            Ref = x.Ref
        }).ToList();

        vm.TransactionsWithoutPaymentRows = txWithoutPayments.Take(250).Select(x => new FinancialMismatchRow
        {
            CompanyId = x.CompanyId,
            Company = x.CompanyId.HasValue && companies.TryGetValue(x.CompanyId.Value, out var name) ? name : "",
            PaymentId = null,
            TransactionId = x.TransactionIds.FirstOrDefault(),
            ReservationId = x.ReservationId,
            Amount = x.TotalAmount,
            Status = "Revenue",
            Ref = $"TxnIds: {string.Join(",", x.TransactionIds)}"
        }).ToList();

        vm.ConfirmedReservationWithoutPayRows = confirmedWithoutSucceededPayment.Take(250).Select(x => new ConfirmedReservationNoPayRow
        {
            CompanyId = x.CompanyId,
            Company = companies.TryGetValue(x.CompanyId, out var name) ? name : "",
            ReservationId = x.ReservationId,
            Guest = x.Guest,
            Amount = x.Amount,
            Status = x.Status
        }).ToList();

        vm.DuplicateRevenueGroups = revenueTxnByReservation
            .Where(x => x.TransactionIds.Count > 1)
            .Take(250)
            .Select(x => new DuplicateRevenueGroupRow
            {
                CompanyId = x.CompanyId,
                Company = x.CompanyId.HasValue && companies.TryGetValue(x.CompanyId.Value, out var name) ? name : "",
                ReservationId = x.ReservationId,
                TransactionIds = x.TransactionIds,
                TotalAmount = x.TotalAmount
            })
            .ToList();

        vm.OrphanTransactionRows = orphanTransactions.Take(250).Select(x => new ValidationRow
        {
            Entity = "Transaction",
            CompanyId = x.CompanyId,
            Company = "",
            RecordId = x.TransactionId,
            Issue = "Missing company",
            Ref = $"{x.ReferenceType}:{x.ReferenceId}"
        }).ToList();

        vm.DuplicateRevenueTransactionRows = duplicateRevenue.Take(250).Select(x => new ValidationRow
        {
            Entity = "Transaction",
            CompanyId = x.CompanyId,
            Company = x.CompanyId.HasValue && companies.TryGetValue(x.CompanyId.Value, out var name) ? name : "",
            RecordId = x.TransactionId,
            Issue = "Duplicate revenue for reservation",
            Ref = x.ReferenceId.HasValue ? $"Reservation #{x.ReferenceId.Value}" : ""
        }).ToList();

        if (vm.DuplicateRevenueGroups.Count > 0)
        {
            vm.DuplicateRevenueTransactionRows = vm.DuplicateRevenueGroups
                .SelectMany(g => g.TransactionIds.Select(tid => new ValidationRow
                {
                    Entity = "Transaction",
                    CompanyId = g.CompanyId,
                    Company = g.Company,
                    RecordId = tid,
                    Issue = "Duplicate revenue for reservation",
                    Ref = $"Reservation #{g.ReservationId} (Group: {string.Join(",", g.TransactionIds)})"
                }))
                .Take(250)
                .ToList();
        }

        vm.OrphanWorkflowRows = wfOrphans.Take(250).Select(x => new ValidationRow
        {
            Entity = "WorkflowInstance",
            CompanyId = x.CompanyId,
            Company = x.CompanyId.HasValue && companies.TryGetValue(x.CompanyId.Value, out var name) ? name : "",
            RecordId = x.InstanceId,
            Issue = "Missing reservation reference",
            Ref = x.ReferenceId.HasValue ? $"Reservation #{x.ReferenceId.Value}" : ""
        }).ToList();

        vm.OrphanServiceRequestRows = orphanServiceRequests.Take(250).Select(x => new ValidationRow
        {
            Entity = "ServiceRequest",
            CompanyId = x.CompanyId,
            Company = x.CompanyId.HasValue && companies.TryGetValue(x.CompanyId.Value, out var name) ? name : "",
            RecordId = x.RequestId,
            Issue = "Missing company/guest/service",
            Ref = x.Status ?? ""
        }).ToList();

        vm.FailedPaymentRows = failedPayments.Take(250).Select(x => new ValidationRow
        {
            Entity = "Payment",
            CompanyId = x.CompanyId,
            Company = x.CompanyId.HasValue && companies.TryGetValue(x.CompanyId.Value, out var name) ? name : "",
            RecordId = x.PaymentId,
            Issue = x.Status ?? "Failed",
            Ref = x.StripePaymentIntentId ?? ""
        }).ToList();

        vm.PendingRefundRows = pendingRefunds.Take(250).Select(x => new ValidationRow
        {
            Entity = "Payment",
            CompanyId = x.CompanyId,
            Company = x.CompanyId.HasValue && companies.TryGetValue(x.CompanyId.Value, out var name) ? name : "",
            RecordId = x.PaymentId,
            Issue = x.Status ?? "Refund Pending",
            Ref = x.StripePaymentIntentId ?? ""
        }).ToList();

        if (escalate && performedByUserId.HasValue)
        {
            var totalIssues = vm.OrphanReservations + vm.OrphanPayments + vm.OrphanTransactions + vm.DuplicateRevenueTransactions + vm.OrphanWorkflows + vm.IncompleteWorkflowSteps + vm.OrphanServiceRequests + vm.FailedPayments + vm.PendingRefunds;
            if (totalIssues > 0)
            {
                await _notify.NotifySuperAdminsAsync(
                    title: "System Validation Detected Issues",
                    message: $"SystemValidation run detected {totalIssues} issue(s). Review SuperAdmin/SystemValidation.",
                    type: "Alert",
                    ct);

                var affectedCompanyIds = new HashSet<int>();
                foreach (var c in vm.OrphanReservationRows.Select(r => r.CompanyId).Concat(vm.OrphanPaymentRows.Select(r => r.CompanyId)).Concat(vm.DuplicateRevenueTransactionRows.Select(r => r.CompanyId)).Concat(vm.OrphanWorkflowRows.Select(r => r.CompanyId)).Concat(vm.OrphanServiceRequestRows.Select(r => r.CompanyId)).Concat(vm.FailedPaymentRows.Select(r => r.CompanyId)).Concat(vm.PendingRefundRows.Select(r => r.CompanyId)))
                {
                    if (c.HasValue) affectedCompanyIds.Add(c.Value);
                }

                foreach (var cid in affectedCompanyIds)
                {
                    await _notify.NotifyRoleAsync(cid, roleId: 2,
                        title: "Integrity Issues Detected",
                        message: "Integrity validation detected data issues related to your company. Please review.",
                        type: "Alert",
                        ct);

                    await _notify.NotifyRoleAsync(cid, roleId: 3,
                        title: "Integrity Issues Detected",
                        message: "Integrity validation detected accounting/data issues related to your company. Please review.",
                        type: "Alert",
                        ct);
                }
            }
        }

         return vm;
    }
}
