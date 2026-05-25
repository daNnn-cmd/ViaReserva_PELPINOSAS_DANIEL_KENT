using System.Collections.Generic;
using ViaReservaERP.Models;

namespace ViaReservaERP.Models.Admin;

public class CheckInViewModel
{
    public Reservation Reservation { get; set; } = null!;
    public decimal TotalAmount { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Balance => TotalAmount - TotalPaid;
    
    public bool IsPaymentVerified => Balance <= 0;
    public bool IsRoomAssigned { get; set; }
    public string? AssignedRoomNumber { get; set; }
    
    public bool CanCheckIn => Reservation.Status == "Confirmed" && IsRoomAssigned;
}
