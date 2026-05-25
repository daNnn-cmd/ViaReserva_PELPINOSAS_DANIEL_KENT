using ViaReservaERP.Models;

namespace ViaReservaERP.Models.FrontDesk;

public class CheckInOutViewModel
{
    public DateOnly Date { get; set; }

    public List<Reservation> Arrivals { get; set; } = new();
    public List<Reservation> InHouse { get; set; } = new();
    public List<Reservation> Departures { get; set; } = new();
}
