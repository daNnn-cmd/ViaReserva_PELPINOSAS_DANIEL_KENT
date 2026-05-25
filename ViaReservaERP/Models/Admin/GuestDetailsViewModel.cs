using System.Collections.Generic;
using ViaReservaERP.Models;

namespace ViaReservaERP.Models.Admin;

public class GuestDetailsViewModel
{
    public Guest Guest { get; set; } = null!;
    public List<Reservation> RecentReservations { get; set; } = new();
    public List<ServiceRequest> RecentRequests { get; set; } = new();
    
    public decimal TotalSpent { get; set; }
    public int StayCount { get; set; }
    public string LoyaltyTier { get; set; } = "Standard";
}
