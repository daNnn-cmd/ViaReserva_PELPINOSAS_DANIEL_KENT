using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using ViaReservaERP.Models;

namespace ViaReservaERP.Models.Admin;

public class RoomAssignmentViewModel
{
    public int ReservationId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public DateOnly CheckInDate { get; set; }
    public DateOnly CheckOutDate { get; set; }
    
    public int? CurrentRoomId { get; set; }
    public string CurrentRoomNumber { get; set; } = "None";
    
    public List<AvailableRoomItem> AvailableRooms { get; set; } = new();
    public int SelectedRoomId { get; set; }
}

public class AvailableRoomItem
{
    public int RoomId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public bool IsUpgrade { get; set; }
}
