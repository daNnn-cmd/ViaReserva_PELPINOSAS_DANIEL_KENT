namespace ViaReservaERP.Models.SuperAdmin;

public class SubscriptionDetailsViewModel
{
    public int SubscriptionId { get; set; }
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    public string PlanName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int? DurationMonths { get; set; }

    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StripeSubscriptionId { get; set; } = string.Empty;
}

public class SubscriptionBillingHistoryViewModel
{
    public int SubscriptionId { get; set; }
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    public List<SubscriptionBillingHistoryRow> Rows { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class SubscriptionBillingHistoryRow
{
    public int PaymentId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string StripePaymentIntentId { get; set; } = string.Empty;
}

public class SubscriptionUsageReportViewModel
{
    public int SubscriptionId { get; set; }
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    public int Users { get; set; }
    public int Reservations { get; set; }
    public int WorkflowInstances { get; set; }
    public int ServiceRequests { get; set; }
    public int AuditLogs { get; set; }

    public List<UsageModuleRow> Modules { get; set; } = new();
}

public class UsageModuleRow
{
    public string Module { get; set; } = string.Empty;
    public int Volume { get; set; }
}

public class SubscriptionAuditLogsViewModel
{
    public int SubscriptionId { get; set; }
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;

    public List<SubscriptionAuditLogRow> Rows { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class SubscriptionAuditLogRow
{
    public DateTime TimestampUtc { get; set; }
    public string Company { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
    public int? RecordId { get; set; }
    public string IpAddress { get; set; } = string.Empty;
}
