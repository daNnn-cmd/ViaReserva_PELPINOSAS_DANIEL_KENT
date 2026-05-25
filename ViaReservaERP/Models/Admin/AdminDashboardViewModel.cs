using System.Collections.Generic;

namespace ViaReservaERP.Models.Admin;

public class AdminDashboardViewModel
{
    public string Granularity { get; set; } = "month";
    public int? SelectedYear { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string SelectedRangeLabel { get; set; } = string.Empty;

    public DateOnly CompareStartDate { get; set; }
    public DateOnly CompareEndDate { get; set; }

    public int ReservationsCount { get; set; }
    public double? ReservationsDeltaPct { get; set; }

    public int GuestsCount { get; set; }
    public double? GuestsDeltaPct { get; set; }

    public int StaffCount { get; set; }
    public double? StaffDeltaPct { get; set; }

    public int PendingRequestsCount { get; set; }
    public double? PendingRequestsDeltaPct { get; set; }
    
    // New KPIs
    public int ActiveStays { get; set; }
    public decimal OccupancyRate { get; set; }
    public double? OccupancyRateDeltaPct { get; set; }

    public decimal RevenueToday { get; set; }
    public decimal RevenueMonth { get; set; }
    public double? RevenueMonthDeltaPct { get; set; }

    public int CancelledBookings { get; set; }
    public int PendingApprovals { get; set; }
    public double? PendingApprovalsDeltaPct { get; set; }
    
    public decimal TotalRevenue { get; set; }
    public decimal MonthlyProfit { get; set; }

    public ChartSeries RevenueAnalytics { get; set; } = new();
    public ChartSeries ReservationAnalytics { get; set; } = new();
    public ChartSeries ServiceAnalytics { get; set; } = new();
    public ChartSeries WorkflowAnalytics { get; set; } = new();
    public ChartSeries FinancialAnalytics { get; set; } = new();
    
    public List<ReservationSummary> RecentReservations { get; set; } = new();
    public List<ActivityLogSummary> RecentActivities { get; set; } = new();
    
    // Legacy support
    public List<ChartDataPoint> BookingTrends { get; set; } = new();
    public List<ChartDataPoint> RevenueTrends { get; set; } = new();
}


public class ChartDataPoint
{
    public string Label { get; set; } = string.Empty;
    public decimal Value { get; set; }
}

public class ReservationSummary
{
    public int ReservationId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime Date { get; set; }
    public decimal Amount { get; set; }
}

public class ActivityLogSummary
{
    public string Time { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
}
