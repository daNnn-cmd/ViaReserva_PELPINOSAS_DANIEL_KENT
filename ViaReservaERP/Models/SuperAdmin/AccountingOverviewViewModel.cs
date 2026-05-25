namespace ViaReservaERP.Models.SuperAdmin;

public class AccountingOverviewViewModel
{
    public string Granularity { get; set; } = "month";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string SelectedRangeLabel { get; set; } = string.Empty;

    public int? CompanyId { get; set; }
    public string CompanyName { get; set; } = "All Companies";
    public List<CompanyOptionRow> Companies { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);

    public decimal TotalRevenue { get; set; }
    public decimal ReservationRevenue { get; set; }
    public decimal GuestServiceRevenue { get; set; }
    public decimal SubscriptionRevenue { get; set; }
    public decimal Refunds { get; set; }
    public decimal NetProfit { get; set; }

    public List<AccountingTransactionRow> Transactions { get; set; } = new();

    public AccountingExpenseSummary Expenses { get; set; } = new();
    public AccountingAlertsSummary Alerts { get; set; } = new();

    public AccountingWorkflowTrackerSummary Workflow { get; set; } = new();

    public ChartSeries RevenueBreakdownAnalytics { get; set; } = new();
    public ChartSeries RevenueGrowthAnalytics { get; set; } = new();
    public ChartSeries YearlyTrendsAnalytics { get; set; } = new();
}

public class AccountingTransactionRow
{
    public string Source { get; set; } = string.Empty; // payment | transaction
    public int Id { get; set; }

    public string Company { get; set; } = string.Empty;
    public string TransactionType { get; set; } = string.Empty;
    public string RelatedModule { get; set; } = string.Empty;
    public string CustomerOrGuest { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime TransactionDateUtc { get; set; }

    public int? ReservationId { get; set; }
}

public class AccountingExpenseSummary
{
    public decimal OperationalExpenses { get; set; }
    public decimal RefundLosses { get; set; }
    public decimal ServiceOperationalCosts { get; set; }
    public decimal CompanyLevelExpenses { get; set; }
}

public class AccountingAlertsSummary
{
    public int FailedPayments { get; set; }
    public int HighRefundRateCompanies { get; set; }
    public int SuspiciousTransactions { get; set; }
    public int SubscriptionPaymentFailures { get; set; }
    public int RevenueDeclineAlerts { get; set; }
}

public class AccountingWorkflowTrackerSummary
{
    public int ReservationPayments { get; set; }
    public int ServiceCharges { get; set; }
    public int RefundProcessing { get; set; }
    public int FinancialRecording { get; set; }
    public int AuditLogging { get; set; }
    public int Reporting { get; set; }
}
