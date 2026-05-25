namespace ViaReservaERP.Models.SuperAdmin;

public class AuditLogDetailsViewModel
{
    public int AuditId { get; set; }
    public DateTime TimestampUtc { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;

    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string TableName { get; set; } = string.Empty;
    public int? RecordId { get; set; }

    public string IpAddress { get; set; } = string.Empty;
    public string UserAgent { get; set; } = string.Empty;

    public string OldValues { get; set; } = string.Empty;
    public string NewValues { get; set; } = string.Empty;
}

public class AuditUserActivityHistoryViewModel
{
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;

    public List<AuditLogMonitoringRow> Rows { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class AuditModuleActivityHistoryViewModel
{
    public string Module { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public List<AuditLogMonitoringRow> Rows { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class AuditSecurityIncidentDetailsViewModel
{
    public string IncidentType { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;
    public List<AuditLogMonitoringRow> Rows { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}
