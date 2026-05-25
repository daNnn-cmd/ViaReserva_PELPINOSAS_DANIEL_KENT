using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc.Rendering;
using ViaReservaERP.Models.Admin;

namespace ViaReservaERP.Models.FrontDesk;

public class WalkInReservationViewModel
{
    public int? SelectedGuestId { get; set; }
    public string? NewGuestFullName { get; set; }
    public string? NewGuestEmail { get; set; }

    [RegularExpression(@"^[^a-zA-Z]*$", ErrorMessage = "Guest phone number cannot contain letters.")]
    public string? NewGuestPhone { get; set; }
    public bool CreatePortalAccount { get; set; } = true;
    public string? NewGuestPassword { get; set; }

    public int SelectedRoomId { get; set; }

    public DateOnly CheckInDate { get; set; } = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);
    public DateOnly CheckOutDate { get; set; } = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date.AddDays(1));

    public int NumberOfGuests { get; set; } = 1;
    public string? SpecialRequests { get; set; }

    public bool CheckInNow { get; set; } = true;

    public List<SelectListItem> Guests { get; set; } = new();
    public List<RoomSelectionItem> AvailableRooms { get; set; } = new();
}
