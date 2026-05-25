using System.ComponentModel.DataAnnotations;

namespace ViaReservaERP.Models.Enterprise;

public class EnterpriseInquiryViewModel
{
    [Required]
    [MaxLength(150)]
    public string CompanyName { get; set; } = string.Empty;

    [Required]
    [MaxLength(150)]
    public string ContactPerson { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MaxLength(50)]
    public string Phone { get; set; } = string.Empty;

    [Required]
    [Range(1, 100000)]
    public int NumberOfBranches { get; set; }

    [Required]
    [MaxLength(2000)]
    public string Requirements { get; set; } = string.Empty;

    [MaxLength(2000)]
    public string? CustomWorkflowNeeds { get; set; }
}
