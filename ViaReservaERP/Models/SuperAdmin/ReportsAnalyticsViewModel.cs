namespace ViaReservaERP.Models.SuperAdmin;

public class ReportsAnalyticsViewModel
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string SelectedRangeLabel { get; set; } = string.Empty;

    public int? CompanyId { get; set; }
    public string CompanyName { get; set; } = "All Companies";
    public List<CompanyOptionRow> Companies { get; set; } = new();

    public decimal TotalRevenue { get; set; }
    public int TotalReservations { get; set; }
    public decimal GuestSatisfactionRate { get; set; }
    public int ActiveSubscriptions { get; set; }
    public decimal WorkflowCompletionRate { get; set; }
    public decimal ComplianceScore { get; set; }

    public ChartSeries ReservationTrends { get; set; } = new();
    public ChartSeries ServiceRequestTrends { get; set; } = new();

    public ChartSeries RevenueTrends { get; set; } = new();
    public ChartSeries RefundTrends { get; set; } = new();
    public ChartSeries SubscriptionRevenueTrends { get; set; } = new();

    public ChartSeries SubscriptionTrends { get; set; } = new();
    public ChartSeries SubscriptionChurnTrends { get; set; } = new();
    public ChartSeries PlanDistribution { get; set; } = new();

    public ChartSeries WorkflowTrends { get; set; } = new();

    public ChartSeries ComplianceTrends { get; set; } = new();
    public ChartSeries ModuleActivityDistribution { get; set; } = new();

    public List<CompanyPerformanceRankRow> CompanyRankings { get; set; } = new();

    public ForecastCards Forecast { get; set; } = new();
}

public class CompanyPerformanceRankRow
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public decimal Revenue { get; set; }
    public int Reservations { get; set; }
    public decimal GuestRating { get; set; }
    public decimal WorkflowEfficiency { get; set; }
    public string SubscriptionStatus { get; set; } = string.Empty;
    public decimal GrowthRate { get; set; }
}

public class ForecastCards
{
    public decimal ExpectedMonthlyRevenue { get; set; }
    public decimal ReservationDemandForecast { get; set; }
    public decimal SubscriptionRenewalForecast { get; set; }
    public decimal OperationalGrowthTrend { get; set; }
}
