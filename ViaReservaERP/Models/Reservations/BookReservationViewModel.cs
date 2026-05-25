using System.ComponentModel.DataAnnotations;

namespace ViaReservaERP.Models.Reservations;

public class BookReservationViewModel
{
    [Required]
    public int CompanyId { get; set; }

    [Required]
    public int SelectedRoomTypeId { get; set; }

    [Required]
    public int SelectedRoomId { get; set; }

    [Required]
    public decimal SelectedRoomTypePrice { get; set; }

    public string SelectedServicesJson { get; set; } = "[]";

    public string? StripePaymentIntentId { get; set; }

    public bool DemoPayment { get; set; }

    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [RegularExpression(@"^[0-9]{11}$", ErrorMessage = "Phone number must be exactly 11 numeric digits.")]
    [MaxLength(11)]
    public string Phone { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    public string? Password { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateOnly CheckInDate { get; set; }

    [Required]
    [DataType(DataType.Date)]
    public DateOnly CheckOutDate { get; set; }
}
