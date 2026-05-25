namespace ViaReservaERP.Models.SuperAdmin;

public class ReservationMonitoringViewModel
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

    public int TotalReservations { get; set; }
    public int ActiveBookings { get; set; }
    public int CheckedInGuests { get; set; }
    public int CompletedReservations { get; set; }
    public int CancelledReservations { get; set; }
    public decimal ReservationRevenue { get; set; }

    public List<ReservationRow> Reservations { get; set; } = new();

    public ReservationWorkflowSummary Workflow { get; set; } = new();
    public ReservationAlertsSummary Alerts { get; set; } = new();

    public ChartSeries ReservationVolumeAnalytics { get; set; } = new();
    public ChartSeries RevenueTrendAnalytics { get; set; } = new();
    public ChartSeries CancellationTrendAnalytics { get; set; } = new();
}

public class CompanyOptionRow
{
    public int CompanyId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
}

public class ReservationRow
{
    public int ReservationId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string HotelCompany { get; set; } = string.Empty;
    public string RoomType { get; set; } = string.Empty;
    public DateOnly? CheckIn { get; set; }
    public DateOnly? CheckOut { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
    public string ReservationStatus { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
}

public class ReservationWorkflowSummary
{
    public int BookingCreated { get; set; }
    public int PaymentProcessed { get; set; }
    public int ReservationConfirmed { get; set; }
    public int CheckIn { get; set; }
    public int CheckOut { get; set; }
    public int AccountingRecorded { get; set; }
}

public class ReservationAlertsSummary
{
    public int Overbookings { get; set; }
    public int FailedPayments { get; set; }
    public int RefundRequests { get; set; }
    public int ReservationConflicts { get; set; }
    public int GuestComplaints { get; set; }
}
