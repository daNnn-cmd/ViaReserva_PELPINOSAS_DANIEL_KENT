namespace ViaReservaERP.Models.SuperAdmin;

public class ReservationDetailsViewModel
{
    public int ReservationId { get; set; }
    public string HotelCompany { get; set; } = string.Empty;

    public string GuestName { get; set; } = string.Empty;
    public string GuestEmail { get; set; } = string.Empty;
    public string GuestPhone { get; set; } = string.Empty;

    public DateOnly? CheckInDate { get; set; }
    public DateOnly? CheckOutDate { get; set; }
    public DateTime? BookingDateUtc { get; set; }

    public string ReservationStatus { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }

    public List<RoomDetailRow> Rooms { get; set; } = new();
    public List<SpecialRequestRow> SpecialRequests { get; set; } = new();

    public string AssignedStaffSummary { get; set; } = string.Empty;

    public List<BookingTimelineRow> BookingTimeline { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class RoomDetailRow
{
    public string RoomNumber { get; set; } = string.Empty;
    public string RoomType { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class SpecialRequestRow
{
    public string Item { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}

public class BookingTimelineRow
{
    public DateTime TimestampUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
}

public class PaymentDetailsViewModel
{
    public int PaymentId { get; set; }
    public int? ReservationId { get; set; }

    public string GuestName { get; set; } = string.Empty;
    public string HotelCompany { get; set; } = string.Empty;

    public string PaymentMethod { get; set; } = string.Empty;
    public string StripeReference { get; set; } = string.Empty;
    public string PaymentStatus { get; set; } = string.Empty;

    public decimal AmountPaid { get; set; }
    public DateTime BillingDateUtc { get; set; }

    public List<RefundHistoryRow> RefundHistory { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class RefundHistoryRow
{
    public DateTime TimestampUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Actor { get; set; } = string.Empty;
    public string ActorRole { get; set; } = string.Empty;
}

public class ReservationServiceRequestsViewModel
{
    public int ReservationId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string HotelCompany { get; set; } = string.Empty;

    public List<ServiceRequestRow> Rows { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class ServiceRequestRow
{
    public int RequestId { get; set; }
    public string RequestedService { get; set; } = string.Empty;
    public string AssignedServiceStaff { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedDateUtc { get; set; }
    public DateTime? CompletionDateUtc { get; set; }
    public string Notes { get; set; } = string.Empty;
}

public class ReservationAuditLogsViewModel
{
    public int ReservationId { get; set; }
    public string HotelCompany { get; set; } = string.Empty;

    public List<AuditLogRow> Rows { get; set; } = new();
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class AuditLogRow
{
    public DateTime TimestampUtc { get; set; }
    public string Action { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Company { get; set; } = string.Empty;
    public string Table { get; set; } = string.Empty;
}
