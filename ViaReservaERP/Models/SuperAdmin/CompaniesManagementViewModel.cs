using ViaReservaERP.Models;

namespace ViaReservaERP.Models.SuperAdmin;

public class CompaniesManagementViewModel
{
    public int TotalCompanies { get; set; }
    public int ActiveCompanies { get; set; }
    public int InactiveCompanies { get; set; }
    public int ExpiringSubscriptions { get; set; }

    public string? Search { get; set; }
    public string? SubscriptionStatus { get; set; }
    public bool? IsActive { get; set; }

    public string Sort { get; set; } = "created";
    public string Dir { get; set; } = "desc";

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }

    public List<Company> Rows { get; set; } = new();

    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}
