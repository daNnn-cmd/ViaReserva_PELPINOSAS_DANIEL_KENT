namespace ViaReservaERP.Models.SuperAdmin;

public class AuditLogsViewModel
{
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

    public int TotalLogsToday { get; set; }
    public int FailedActivities { get; set; }
    public int SecurityAlerts { get; set; }
    public int LoginAttempts { get; set; }
    public int WorkflowErrors { get; set; }
    public int CriticalEvents { get; set; }

    public List<AuditLogMonitoringRow> Rows { get; set; } = new();

    public AuditSecurityMonitoringSummary Security { get; set; } = new();
    public AuditModuleActivitySummary ModuleActivity { get; set; } = new();
    public AuditCriticalAlertsSummary Alerts { get; set; } = new();

    public ChartSeries DailyLogVolumeAnalytics { get; set; } = new();
    public ChartSeries MonthlyActivityTrendsAnalytics { get; set; } = new();
    public ChartSeries SecurityIncidentsAnalytics { get; set; } = new();
    public ChartSeries ModuleDistributionAnalytics { get; set; } = new();
    public ChartSeries ErrorFrequencyAnalytics { get; set; } = new();
}

public class AuditLogMonitoringRow
{
    public int AuditId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string ActionPerformed { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string DeviceBrowser { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
    public DateTime TimestampUtc { get; set; }
}

public class AuditSecurityMonitoringSummary
{
    public int FailedLoginAttempts { get; set; }
    public int UnauthorizedAccessAttempts { get; set; }
    public int SuspiciousTransactions { get; set; }
    public int MultipleLoginLocations { get; set; }
    public int AccountLockouts { get; set; }
}

public class AuditModuleActivitySummary
{
    public int Reservations { get; set; }
    public int GuestServices { get; set; }
    public int WorkflowManagement { get; set; }
    public int Accounting { get; set; }
    public int SubscriptionManagement { get; set; }
    public int UserManagement { get; set; }
}

public class AuditCriticalAlertsSummary
{
    public int FraudIndicators { get; set; }
    public int DataModificationAttempts { get; set; }
    public int FailedPaymentSpikes { get; set; }
    public int UnauthorizedRoleChanges { get; set; }
    public int RepeatedWorkflowFailures { get; set; }
}
