using ViaReservaERP.Models;

namespace ViaReservaERP.Models.SuperAdmin;

public class EnterpriseInquiriesViewModel
{
    public string? Search { get; set; }
    public string? Status { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);

    public int TotalInquiries { get; set; }
    public int NewInquiries { get; set; }
    public int ContactedInquiries { get; set; }
    public int ClosedInquiries { get; set; }

    public List<EnterpriseInquiry> Rows { get; set; } = new();
}
