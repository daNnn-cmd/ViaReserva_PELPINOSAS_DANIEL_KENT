namespace ViaReservaERP.Models.SuperAdmin;

public class GuestServiceMonitoringViewModel
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

    public int TotalServiceRequests { get; set; }
    public int PendingRequests { get; set; }
    public int CompletedRequests { get; set; }
    public int InProgressRequests { get; set; }
    public int EscalatedRequests { get; set; }
    public double AverageResponseTimeHours { get; set; }

    public List<ServiceRequestMonitoringRow> Requests { get; set; } = new();

    public ServiceWorkflowSummary Workflow { get; set; } = new();
    public ServiceAlertsSummary Alerts { get; set; } = new();

    public ChartSeries DailyRequestsAnalytics { get; set; } = new();
    public ChartSeries TrendRequestsAnalytics { get; set; } = new();
    public ChartSeries MostRequestedServicesAnalytics { get; set; } = new();
    public ChartSeries AvgCompletionTimeAnalytics { get; set; } = new();
    public ChartSeries GuestSatisfactionSignalsAnalytics { get; set; } = new();
}

public class ServiceRequestMonitoringRow
{
    public int RequestId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string HotelCompany { get; set; } = string.Empty;
    public int? ReservationId { get; set; }
    public string ServiceType { get; set; } = string.Empty;
    public string AssignedStaff { get; set; } = string.Empty;
    public int? AssignedStaffUserId { get; set; }
    public DateTime RequestDateUtc { get; set; }
    public DateTime? CompletionDateUtc { get; set; }
    public string Status { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
}

public class ServiceWorkflowSummary
{
    public int GuestRequestCreated { get; set; }
    public int StaffAssigned { get; set; }
    public int ServiceInProgress { get; set; }
    public int ServiceCompleted { get; set; }
    public int FeedbackSubmitted { get; set; }
    public int AuditLogged { get; set; }
}

public class ServiceAlertsSummary
{
    public int DelayedRequests { get; set; }
    public int EscalatedComplaints { get; set; }
    public int UnassignedRequests { get; set; }
    public int FailedServiceCompletion { get; set; }
    public int NegativeGuestFeedback { get; set; }
}
