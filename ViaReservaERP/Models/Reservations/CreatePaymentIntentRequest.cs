using System.ComponentModel.DataAnnotations;

namespace ViaReservaERP.Models.Reservations;

public class CreatePaymentIntentRequest
{
    [Required]
    public int CompanyId { get; set; }

    [Required]
    public int SelectedRoomTypeId { get; set; }

    [Required]
    public decimal SelectedRoomTypePrice { get; set; }

    [Required]
    public DateOnly CheckInDate { get; set; }

    [Required]
    public DateOnly CheckOutDate { get; set; }

    public string SelectedServicesJson { get; set; } = "[]";

    [Required]
    public string Email { get; set; } = string.Empty;
}
