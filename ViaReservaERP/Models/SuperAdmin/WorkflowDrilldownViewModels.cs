namespace ViaReservaERP.Models.SuperAdmin;

public class WorkflowDetailsViewModel
{
    public int InstanceId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string WorkflowName { get; set; } = string.Empty;
    public string WorkflowDescription { get; set; } = string.Empty;

    public string ReferenceType { get; set; } = string.Empty;
    public int? ReferenceId { get; set; }

    public string Status { get; set; } = string.Empty;
    public int? CurrentStep { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? CompletedAtUtc { get; set; }

    public List<WorkflowStepHistoryRow> StepHistory { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class WorkflowStepHistoryRow
{
    public DateTime PerformedAtUtc { get; set; }
    public string StepName { get; set; } = string.Empty;
    public int? StepOrder { get; set; }
    public string ActionTaken { get; set; } = string.Empty;
    public string PerformedBy { get; set; } = string.Empty;
    public string PerformedByRole { get; set; } = string.Empty;
}

public class WorkflowApprovalHistoryViewModel
{
    public int InstanceId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public List<WorkflowStepHistoryRow> Rows { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class WorkflowAuditLogsViewModel
{
    public int InstanceId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public List<AuditLogRow> Rows { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class WorkflowDepartmentTasksViewModel
{
    public int InstanceId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public List<WorkflowStepHistoryRow> Rows { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}
