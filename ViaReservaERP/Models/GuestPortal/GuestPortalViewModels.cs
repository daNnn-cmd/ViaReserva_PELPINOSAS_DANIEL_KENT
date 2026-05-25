namespace ViaReservaERP.Models.GuestPortal;

public class GuestDashboardViewModel
{
    public int GuestId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string CompanyName { get; set; } = string.Empty;

    public int UpcomingBookings { get; set; }
    public int ActiveStays { get; set; }
    public int CompletedStays { get; set; }
    public int PendingServices { get; set; }

    public decimal TotalPaid { get; set; }

    public int? HighlightReservationId { get; set; }
    public ReservationSummaryCard? HighlightReservation { get; set; }

    public List<ReservationRow> RecentReservations { get; set; } = new();
    public List<ServiceRequestRow> RecentServiceRequests { get; set; } = new();
    public List<PaymentRow> RecentPayments { get; set; } = new();
}

public class ReservationRow
{
    public int ReservationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateOnly? CheckInDate { get; set; }
    public DateOnly? CheckOutDate { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ServiceCharge { get; set; }
}

public class ReservationSummaryCard
{
    public int ReservationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateOnly? CheckInDate { get; set; }
    public DateOnly? CheckOutDate { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentStatus { get; set; } = string.Empty;
}

public class ServiceRequestRow
{
    public int RequestId { get; set; }
    public int? ReservationId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public string Status { get; set; } = string.Empty;
    public DateTime RequestedAtUtc { get; set; }
}

public class PaymentRow
{
    public int PaymentId { get; set; }
    public int? ReservationId { get; set; }
    public decimal Amount { get; set; }
    public string Status { get; set; } = string.Empty;
    public string PaymentMethod { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public string StripePaymentIntentId { get; set; } = string.Empty;
}

public class GuestReservationsViewModel
{
    public int GuestId { get; set; }
    public string GuestName { get; set; } = string.Empty;

    public List<ReservationRow> Upcoming { get; set; } = new();
    public List<ReservationRow> Active { get; set; } = new();
    public List<ReservationRow> Completed { get; set; } = new();
    public List<ReservationRow> Cancelled { get; set; } = new();
}

public class GuestPaymentsViewModel
{
    public int GuestId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public string GuestEmail { get; set; } = string.Empty;
    public List<PaymentRow> Rows { get; set; } = new();
    public List<GuestBalanceRow> Balances { get; set; } = new();
}

public class GuestBalanceRow
{
    public int ReservationId { get; set; }
    public string ReservationStatus { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal PaidAmount { get; set; }
    public decimal BalanceAmount { get; set; }
}

public class GuestServicesViewModel
{
    public int GuestId { get; set; }
    public string GuestName { get; set; } = string.Empty;

    public List<ServiceCatalogRow> Catalog { get; set; } = new();
    public List<ServiceRequestRow> Requests { get; set; } = new();
}

public class ServiceCatalogRow
{
    public int ServiceId { get; set; }
    public string ServiceName { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class GuestTrackingViewModel
{
    public int ReservationId { get; set; }
    public string Status { get; set; } = string.Empty;
    public List<TrackingStepRow> Steps { get; set; } = new();
}

public class TrackingStepRow
{
    public string Stage { get; set; } = string.Empty;
    public DateTime WhenUtc { get; set; }
}

public class GuestProfileViewModel
{
    public int GuestId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string? Phone { get; set; }
    public string? AvatarUrl { get; set; }
}

public class GuestNotificationsViewModel
{
    public List<NotificationRow> Items { get; set; } = new();
}

public class NotificationRow
{
    public int NotificationId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAtUtc { get; set; }
}

public class GuestReservationDetailsViewModel
{
    public int ReservationId { get; set; }
    public string CompanyName { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public DateOnly? CheckInDate { get; set; }
    public DateOnly? CheckOutDate { get; set; }
    public decimal TotalAmount { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxAmount { get; set; }
    public decimal ServiceCharge { get; set; }

    /// <summary>Guest can cancel when status is Pending/Confirmed and check-in date is more than 1 day away.</summary>
    public bool CanCancel { get; set; }

    /// <summary>Guest can request a stay extension when status is Checked In.</summary>
    public bool CanExtendStay { get; set; }

    /// <summary>Guest can request an early check-out when status is Checked In.</summary>
    public bool CanEarlyCheckOut { get; set; }

    public List<GuestReservationRoomRow> Rooms { get; set; } = new();
    public List<GuestReservationServiceRow> Services { get; set; } = new();
    public List<PaymentRow> Payments { get; set; } = new();
}

public class GuestReservationRoomRow
{
    public string RoomNumber { get; set; } = string.Empty;
    public string RoomType { get; set; } = string.Empty;
    public decimal Price { get; set; }
}

public class GuestReservationServiceRow
{
    public string ServiceName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal Price { get; set; }
}
