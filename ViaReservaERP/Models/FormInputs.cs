using System.ComponentModel.DataAnnotations;

namespace ViaReservaERP.Models;

public class CompanyCreateInput
{
    [Required]
    [MaxLength(150)]
    public string CompanyName { get; set; } = string.Empty;

    [MaxLength(150)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }

    [MaxLength(255)]
    public string? Address { get; set; }

    [MaxLength(50)]
    public string? SubscriptionStatus { get; set; }

    public bool IsActive { get; set; } = true;
}

public class GuestCreateInput
{
    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [MaxLength(150)]
    [EmailAddress]
    public string? Email { get; set; }

    [MaxLength(50)]
    public string? Phone { get; set; }
}

public class RoomCreateInput
{
    [Required]
    [MaxLength(20)]
    public string RoomNumber { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int RoomTypeId { get; set; }
}
