namespace ViaReservaERP.Models.SuperAdmin;

public class SystemValidationViewModel
{
    public DateTime RunAtUtc { get; set; }

    public int OrphanReservations { get; set; }
    public int OrphanPayments { get; set; }
    public int OrphanTransactions { get; set; }
    public int DuplicateRevenueTransactions { get; set; }
    public int OrphanWorkflows { get; set; }
    public int IncompleteWorkflowSteps { get; set; }
    public int OrphanServiceRequests { get; set; }
    public int FailedPayments { get; set; }
    public int PendingRefunds { get; set; }

    public decimal RevenuePaymentsSucceeded { get; set; }
    public decimal RevenueTransactionsRevenue { get; set; }
    public decimal RevenueDelta { get; set; }

    public int PaymentsWithoutTransactions { get; set; }
    public int TransactionsWithoutPayments { get; set; }
    public int ConfirmedReservationsWithoutSuccessfulPayment { get; set; }

    public List<ValidationRow> OrphanReservationRows { get; set; } = new();
    public List<ValidationRow> OrphanPaymentRows { get; set; } = new();
    public List<ValidationRow> OrphanWorkflowRows { get; set; } = new();
    public List<ValidationRow> DuplicateRevenueTransactionRows { get; set; } = new();
    public List<ValidationRow> OrphanTransactionRows { get; set; } = new();
    public List<ValidationRow> OrphanServiceRequestRows { get; set; } = new();
    public List<ValidationRow> FailedPaymentRows { get; set; } = new();
    public List<ValidationRow> PendingRefundRows { get; set; } = new();

    public List<FinancialMismatchRow> PaymentsWithoutTransactionRows { get; set; } = new();
    public List<FinancialMismatchRow> TransactionsWithoutPaymentRows { get; set; } = new();
    public List<ConfirmedReservationNoPayRow> ConfirmedReservationWithoutPayRows { get; set; } = new();
    public List<DuplicateRevenueGroupRow> DuplicateRevenueGroups { get; set; } = new();
}

public class ValidationRow
{
    public string Entity { get; set; } = string.Empty;
    public int? CompanyId { get; set; }
    public string Company { get; set; } = string.Empty;
    public int? RecordId { get; set; }
    public string Issue { get; set; } = string.Empty;
    public string Ref { get; set; } = string.Empty;
}

public class FinancialMismatchRow
{
    public int? CompanyId { get; set; }
    public string Company { get; set; } = string.Empty;
    public int? PaymentId { get; set; }
    public int? TransactionId { get; set; }
    public int? ReservationId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Ref { get; set; } = string.Empty;
}

public class ConfirmedReservationNoPayRow
{
    public int CompanyId { get; set; }
    public string Company { get; set; } = string.Empty;
    public int ReservationId { get; set; }
    public string Guest { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
}

public class DuplicateRevenueGroupRow
{
    public int? CompanyId { get; set; }
    public string Company { get; set; } = string.Empty;
    public int ReservationId { get; set; }
    public List<int> TransactionIds { get; set; } = new();
    public decimal TotalAmount { get; set; }
}
