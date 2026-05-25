namespace ViaReservaERP.Models.SuperAdmin;

public class WorkflowManagementViewModel
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

    public int TotalActiveWorkflows { get; set; }
    public int CompletedWorkflows { get; set; }
    public int PendingApprovals { get; set; }
    public int EscalatedWorkflows { get; set; }
    public int FailedWorkflows { get; set; }
    public double AverageProcessingTimeHours { get; set; }

    public List<WorkflowMonitoringRow> Rows { get; set; } = new();

    public WorkflowProcessTrackerSummary Tracker { get; set; } = new();
    public WorkflowAlertsSummary Alerts { get; set; } = new();

    public ChartSeries CompletionRatesAnalytics { get; set; } = new();
    public ChartSeries ApprovalDelaysAnalytics { get; set; } = new();
    public ChartSeries AutomationSuccessAnalytics { get; set; } = new();
    public ChartSeries EscalationFrequencyAnalytics { get; set; } = new();
    public ChartSeries DepartmentBottlenecksAnalytics { get; set; } = new();

    public List<CrossModuleIntegrationRow> IntegrationRows { get; set; } = new();
}

public class WorkflowMonitoringRow
{
    public int WorkflowInstanceId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string WorkflowType { get; set; } = string.Empty;
    public string TriggerSource { get; set; } = string.Empty;
    public string AssignedDepartment { get; set; } = string.Empty;
    public string CurrentStage { get; set; } = string.Empty;
    public string ApprovalStatus { get; set; } = string.Empty;
    public string Priority { get; set; } = string.Empty;
    public DateTime CreatedDateUtc { get; set; }
    public DateTime? CompletionDateUtc { get; set; }
    public int? ReferenceId { get; set; }
    public string ReferenceType { get; set; } = string.Empty;
}

public class WorkflowProcessTrackerSummary
{
    public int RequestCreated { get; set; }
    public int ApprovalRouting { get; set; }
    public int DepartmentAssignment { get; set; }
    public int TaskProcessing { get; set; }
    public int Completion { get; set; }
    public int AuditLogging { get; set; }
}

public class WorkflowAlertsSummary
{
    public int PendingApprovals { get; set; }
    public int DelayedApprovals { get; set; }
    public int EscalatedRequests { get; set; }
    public int FailedAutomations { get; set; }
    public int SlaViolations { get; set; }
}

public class CrossModuleIntegrationRow
{
    public string Module { get; set; } = string.Empty;
    public int Workflows { get; set; }
    public int Pending { get; set; }
    public int Escalated { get; set; }
    public int Failed { get; set; }
}
