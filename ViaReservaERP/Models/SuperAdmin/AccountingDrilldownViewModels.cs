namespace ViaReservaERP.Models.SuperAdmin;

public class TransactionDetailsViewModel
{
    public string Source { get; set; } = string.Empty;
    public int Id { get; set; }

    public string CompanyName { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public string RelatedModule { get; set; } = string.Empty;
    public string CustomerOrGuest { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime TransactionDateUtc { get; set; }

    public string Description { get; set; } = string.Empty;

    public int? ReservationId { get; set; }
    public string StripePaymentIntentId { get; set; } = string.Empty;
}

public class TransactionPaymentHistoryViewModel
{
    public string Source { get; set; } = string.Empty;
    public int Id { get; set; }

    public string CompanyName { get; set; } = string.Empty;
    public int? ReservationId { get; set; }

    public List<TransactionPaymentHistoryRow> Rows { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class TransactionPaymentHistoryRow
{
    public int PaymentId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StripePaymentIntentId { get; set; } = string.Empty;
}

public class TransactionAuditLogsViewModel
{
    public string Source { get; set; } = string.Empty;
    public int Id { get; set; }

    public string CompanyName { get; set; } = string.Empty;
    public List<TransactionAuditLogRow> Rows { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class TransactionAuditLogRow
{
    public DateTime TimestampUtc { get; set; }
    public string Company { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public int? RecordId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
}

public class TransactionLinkedReservationViewModel
{
    public string Source { get; set; } = string.Empty;
    public int Id { get; set; }

    public int ReservationId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string GuestName { get; set; } = string.Empty;
    public DateOnly? CheckIn { get; set; }
    public DateOnly? CheckOut { get; set; }
    public string Status { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}
