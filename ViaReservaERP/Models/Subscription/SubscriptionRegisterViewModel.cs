using System.ComponentModel.DataAnnotations;

namespace ViaReservaERP.Models.SaaS;

public class SubscriptionRegisterViewModel
{
    [Required]
    public string Plan { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string CompanyName { get; set; } = string.Empty;

    [EmailAddress]
    [MaxLength(150)]
    public string? BusinessEmail { get; set; }

    [Required]
    [StringLength(11, MinimumLength = 11, ErrorMessage = "Phone number must be exactly 11 digits.")]
    [RegularExpression(@"^\d{11}$", ErrorMessage = "Phone number must contain only numbers (11 digits).")]
    [DataType(DataType.PhoneNumber)]
    [Display(Name = "Phone Number")]
    public string PhoneNumber { get; set; } = string.Empty;

    [Required]
    [MaxLength(255)]
    public string HotelAddress { get; set; } = string.Empty;

    [Required]
    [Range(1, 100000)]
    public int NumberOfRooms { get; set; }

    [Required]
    [MaxLength(100)]
    public string BusinessType { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string OwnerManagerName { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string AdminFullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(150)]
    public string AdminEmail { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required]
    [DataType(DataType.Password)]
    [Compare(nameof(Password))]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class SubscriptionSuccessViewModel
{
    public string? CompanyName { get; set; }
    public string? PlanName { get; set; }
    public string Message { get; set; } = string.Empty;
}

public static class SubscriptionPlansCatalog
{
    public static bool IsSupportedPlan(string? plan)
    {
        if (string.IsNullOrWhiteSpace(plan)) return false;
        return plan.Equals("Basic", StringComparison.OrdinalIgnoreCase)
               || plan.Equals("Standard", StringComparison.OrdinalIgnoreCase);
    }

    public static (string PlanName, decimal MonthlyPrice) GetPlan(string plan)
    {
        if (plan.Equals("Basic", StringComparison.OrdinalIgnoreCase))
            return ("Basic", 2999m);

        if (plan.Equals("Standard", StringComparison.OrdinalIgnoreCase))
            return ("Standard", 6999m);

        throw new InvalidOperationException("Unsupported plan.");
    }
}
