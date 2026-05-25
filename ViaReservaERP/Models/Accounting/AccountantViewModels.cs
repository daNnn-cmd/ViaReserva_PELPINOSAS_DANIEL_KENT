using ViaReservaERP.Models;

namespace ViaReservaERP.Models.Accounting;

public class CreateTransactionForm
{
    public string Type { get; set; } = "Income";
    public decimal Amount { get; set; }
    public string? Description { get; set; }

    public int? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }

    public DateOnly TransactionDate { get; set; } = DateOnly.FromDateTime(ViaReservaERP.AppTime.Now.Date);
}

public class AccountantTransactionsViewModel
{
    public string? FilterType { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public List<AccountingTransaction> Rows { get; set; } = new();

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);

    public CreateTransactionForm New { get; set; } = new();
}

public class AccountantReportsViewModel
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public decimal TotalIncome { get; set; }
    public decimal TotalExpense { get; set; }
    public decimal TotalTax { get; set; }
    public decimal TotalServiceCharge { get; set; }
    public decimal Net => TotalIncome - TotalExpense;

    // Consistency with Admin Reports
    public decimal Revenue { get; set; }
    public int TotalReservations { get; set; }
    public int TotalServiceRequests { get; set; }

    public List<AccountingTransaction> Recent { get; set; } = new();

    // Analytics
    public Admin.ChartSeries RevenueAnalytics { get; set; } = new();
    public Admin.ChartSeries ReservationAnalytics { get; set; } = new();
    public Admin.ChartSeries ServiceAnalytics { get; set; } = new();
    public Admin.AnalyticsForecast Forecast { get; set; } = new();
}

public class AccountantAuditLogsViewModel
{
    public string? Search { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public List<Admin.AdminAuditLogRow> Rows { get; set; } = new();
}
