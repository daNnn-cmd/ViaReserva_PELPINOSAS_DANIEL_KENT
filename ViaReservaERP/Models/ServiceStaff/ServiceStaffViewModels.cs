namespace ViaReservaERP.Models.ServiceStaff;

public class AssignedServiceRequestRow
{
    public int RequestId { get; set; }
    public int? GuestId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
    public string RoomNumber { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime RequestDateUtc { get; set; }
}

public class ServiceStaffDashboardViewModel
{
    public int Pending { get; set; }
    public int InProgress { get; set; }
    public int Completed { get; set; }

    public List<AssignedServiceRequestRow> Recent { get; set; } = new();
}
