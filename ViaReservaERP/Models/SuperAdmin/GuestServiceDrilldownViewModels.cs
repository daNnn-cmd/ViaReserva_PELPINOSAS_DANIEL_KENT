namespace ViaReservaERP.Models.SuperAdmin;

public class ServiceRequestDetailsViewModel
{
    public int RequestId { get; set; }
    public string HotelCompany { get; set; } = string.Empty;

    public string GuestName { get; set; } = string.Empty;
    public string GuestEmail { get; set; } = string.Empty;
    public string GuestPhone { get; set; } = string.Empty;

    public int? ReservationId { get; set; }

    public string ServiceType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;

    public string AssignedStaff { get; set; } = string.Empty;
    public string AssignedStaffRole { get; set; } = string.Empty;

    public DateTime RequestDateUtc { get; set; }
    public DateTime? CompletionDateUtc { get; set; }

    public List<ServiceTimelineRow> Timeline { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class ServiceTimelineRow
{
    public DateTime TimestampUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
}

public class AssignedStaffDetailsViewModel
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;

    public List<StaffTaskRow> AssignedTasks { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class StaffTaskRow
{
    public int RequestId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string ServiceType { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime RequestDateUtc { get; set; }
}

public class ServiceRequestAuditLogsViewModel
{
    public int RequestId { get; set; }
    public string HotelCompany { get; set; } = string.Empty;

    public List<ServiceTimelineRow> Rows { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}
