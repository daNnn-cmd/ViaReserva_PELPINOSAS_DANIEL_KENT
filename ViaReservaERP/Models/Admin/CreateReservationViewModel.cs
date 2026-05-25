using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using ViaReservaERP.Models;

namespace ViaReservaERP.Models.Admin;

public class CreateReservationViewModel
{
    // Step 1: Guest
    public int? SelectedGuestId { get; set; }
    public string? NewGuestFullName { get; set; }
    public string? NewGuestEmail { get; set; }
    public string? NewGuestPhone { get; set; }
    
    // Step 2 & 3: Stay Details
    public int SelectedRoomId { get; set; }
    public DateOnly CheckInDate { get; set; } = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);
    public DateOnly CheckOutDate { get; set; } = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date.AddDays(1));
    public int NumberOfGuests { get; set; } = 1;
    public string? SpecialRequests { get; set; }
    
    // Lists for selection
    public List<SelectListItem> Guests { get; set; } = new();
    public List<RoomSelectionItem> AvailableRooms { get; set; } = new();
    
    // Computation
    public decimal RoomBasePrice { get; set; }
    public decimal TotalAmount { get; set; }
}

public class RoomSelectionItem
{
    public int RoomId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public decimal BasePrice { get; set; }
}
