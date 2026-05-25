using ViaReservaERP.Models;
using System.ComponentModel.DataAnnotations;

namespace ViaReservaERP.Models.SuperAdmin;

public class UsersManagementViewModel
{
    public int TotalUsers { get; set; }
    public int ActiveUsers { get; set; }
    public int SuspendedUsers { get; set; }
    public int RecentlyCreatedUsers { get; set; }

    public string? Search { get; set; }
    public int? CompanyId { get; set; }
    public int? RoleId { get; set; }

    public string Sort { get; set; } = "created";
    public string Dir { get; set; } = "desc";

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }

    public List<ErpUser> Rows { get; set; } = new();
    public List<Company> Companies { get; set; } = new();
    public List<Role> Roles { get; set; } = new();

    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);

    public CreateOrEditUserInput CreateUser { get; set; } = new();
    public CreateOrEditUserInput EditUser { get; set; } = new();
}

public class CreateOrEditUserInput
{
    public int? UserId { get; set; }
    [Range(1, int.MaxValue)]
    public int CompanyId { get; set; }

    [Range(1, int.MaxValue)]
    public int RoleId { get; set; }

    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [MinLength(6)]
    public string Password { get; set; } = string.Empty;
    public bool IsActive { get; set; } = true;
}
