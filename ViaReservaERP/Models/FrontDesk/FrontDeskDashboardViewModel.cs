using ViaReservaERP.Models.Admin;

namespace ViaReservaERP.Models.FrontDesk;

public class FrontDeskDashboardViewModel
{
    public int ReservationsToday { get; set; }
    public int CheckInsToday { get; set; }
    public int CheckOutsToday { get; set; }
    public int InHouseGuests { get; set; }

    public List<ReservationSummary> RecentReservations { get; set; } = new();
}
