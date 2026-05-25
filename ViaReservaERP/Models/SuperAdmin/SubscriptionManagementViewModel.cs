namespace ViaReservaERP.Models.SuperAdmin;

public class SubscriptionManagementViewModel
{
    public string Granularity { get; set; } = "month";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string SelectedRangeLabel { get; set; } = string.Empty;

    public int? PlanId { get; set; }
    public string PlanName { get; set; } = "All Plans";
    public List<PlanOptionRow> Plans { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);

    public int ActiveSubscriptions { get; set; }
    public int TrialAccounts { get; set; }
    public int ExpiringSubscriptions { get; set; }
    public int CancelledSubscriptions { get; set; }
    public int FailedPayments { get; set; }
    public decimal MonthlyRecurringRevenue { get; set; }

    public List<SubscriptionMonitoringRow> Rows { get; set; } = new();

    public ChartSeries MrrAnalytics { get; set; } = new();
    public ChartSeries NewSubscriptionsAnalytics { get; set; } = new();
    public ChartSeries ChurnRateAnalytics { get; set; } = new();
    public ChartSeries PlanDistributionAnalytics { get; set; } = new();
    public ChartSeries RenewalTrendsAnalytics { get; set; } = new();

    public SubscriptionAlertsSummary Alerts { get; set; } = new();
    public SubscriptionWorkflowTrackerSummary Workflow { get; set; } = new();

    public List<CompanyUsageRow> UsageRows { get; set; } = new();
}

public class PlanOptionRow
{
    public int PlanId { get; set; }
    public string PlanName { get; set; } = string.Empty;
}

public class SubscriptionMonitoringRow
{
    public int SubscriptionId { get; set; }
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string PlanName { get; set; } = string.Empty;
    public string BillingCycle { get; set; } = string.Empty;
    public DateOnly? StartDate { get; set; }
    public DateOnly? RenewalDate { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string SubscriptionStatus { get; set; } = string.Empty;
    public decimal AmountPaid { get; set; }
}

public class SubscriptionAlertsSummary
{
    public int FailedStripePayments { get; set; }
    public int ExpiringSubscriptions { get; set; }
    public int OverdueInvoices { get; set; }
    public int CancelledRenewals { get; set; }
    public int TrialExpirations { get; set; }
}

public class SubscriptionWorkflowTrackerSummary
{
    public int CompanyRegistrations { get; set; }
    public int PlanSelections { get; set; }
    public int StripePayments { get; set; }
    public int SubscriptionActivations { get; set; }
    public int RenewalProcessing { get; set; }
    public int BillingLogs { get; set; }
    public int AuditLogging { get; set; }
}

public class CompanyUsageRow
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public int Users { get; set; }
    public int ReservationVolume { get; set; }
    public int ActiveModulesUsed { get; set; }
    public int WorkflowInstances { get; set; }
    public int ServiceRequests { get; set; }
}
