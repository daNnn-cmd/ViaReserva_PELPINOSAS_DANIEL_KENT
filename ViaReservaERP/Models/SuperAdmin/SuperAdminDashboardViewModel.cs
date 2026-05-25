namespace ViaReservaERP.Models.SuperAdmin;

public class SuperAdminDashboardViewModel
{
    public string Granularity { get; set; } = "month";
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string SelectedRangeLabel { get; set; } = string.Empty;

    public DateOnly CompareStartDate { get; set; }
    public DateOnly CompareEndDate { get; set; }

    public int TotalHotelCompanies { get; set; }
    public double? TotalHotelCompaniesDeltaPct { get; set; }

    public int TotalActiveUsers { get; set; }
    public double? TotalActiveUsersDeltaPct { get; set; }

    public int TotalReservations { get; set; }
    public double? TotalReservationsDeltaPct { get; set; }

    public decimal TotalRevenue { get; set; }
    public double? TotalRevenueDeltaPct { get; set; }

    public int ActiveSubscriptions { get; set; }
    public double? ActiveSubscriptionsDeltaPct { get; set; }

    public int PendingServiceRequests { get; set; }
    public double? PendingServiceRequestsDeltaPct { get; set; }

    public int WorkflowPendingApprovals { get; set; }
    public double? WorkflowPendingApprovalsDeltaPct { get; set; }

    public int SystemAlerts { get; set; }

    public ChartSeries RevenueAnalytics { get; set; } = new();
    public ChartSeries ReservationAnalytics { get; set; } = new();
    public ChartSeries ServiceAnalytics { get; set; } = new();
    public ChartSeries CompanyGrowthAnalytics { get; set; } = new();
    public ChartSeries WorkflowAnalytics { get; set; } = new();
    public ChartSeries FinancialAnalytics { get; set; } = new();

    public List<RecentActivityRow> RecentActivities { get; set; } = new();
    public List<AuditLogPreviewRow> AuditLogsPreview { get; set; } = new();
}

public class ChartSeries
{
    public List<string> Labels { get; set; } = new();
    public Dictionary<string, List<decimal>> Datasets { get; set; } = new();
}

public class RecentActivityRow
{
    public string Time { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
}

public class AuditLogPreviewRow
{
    public string TableName { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string Browser { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public DateTime ActionDate { get; set; }
}
