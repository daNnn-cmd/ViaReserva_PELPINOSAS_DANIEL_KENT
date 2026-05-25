using System;
using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using ViaReservaERP.Models;

namespace ViaReservaERP.Models.Admin;

public class InventoryViewModel
{
    public List<InventoryRowViewModel> Rows { get; set; } = new();
    public List<RoomType> RoomTypes { get; set; } = new();

    public int TotalAvailable { get; set; }
    public int TotalOccupied { get; set; }
    public int TotalMaintenance { get; set; }
    public int TotalDirty { get; set; }

    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
    public string Sort { get; set; } = "name";
    public string Dir { get; set; } = "asc";
    public string? Search { get; set; }
}

public class InventoryRowViewModel
{
    public Room Room { get; set; } = null!;
    public string? CurrentOccupant { get; set; }
    public int? CurrentReservationId { get; set; }
}

public class RoomFormViewModel
{
    public int RoomId { get; set; }
    public string RoomNumber { get; set; } = string.Empty;
    public int RoomTypeId { get; set; }
    public string Status { get; set; } = "Available";
    public List<SelectListItem> RoomTypeOptions { get; set; } = new();
}

public class StaffManagementViewModel
{
    public List<ErpUser> Rows { get; set; } = new();
    public List<Role> AvailableRoles { get; set; } = new();

    public int TotalStaff { get; set; }
    public int ActiveStaff { get; set; }
    public int RevokedStaff { get; set; }
    public int NewStaffThisMonth { get; set; }

    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
    public string Sort { get; set; } = "name";
    public string Dir { get; set; } = "asc";
    public string? Search { get; set; }
}

public class StaffFormViewModel
{
    [Required]
    [MaxLength(150)]
    public string FullName { get; set; } = string.Empty;

    [Required]
    [EmailAddress]
    [MaxLength(150)]
    public string Email { get; set; } = string.Empty;

    [Required]
    [MinLength(6)]
    public string Password { get; set; } = string.Empty;

    [Range(1, int.MaxValue)]
    public int RoleId { get; set; }
    public List<SelectListItem> RoleOptions { get; set; } = new();
}

public class StaffEditFormViewModel
{
    public int UserId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public bool IsActive { get; set; }
}

public class AccountingDashboardViewModel
{
    public decimal TotalRevenue { get; set; }
    public double? TotalRevenueDeltaPct { get; set; }

    public decimal ServiceRevenue { get; set; }
    public double? ServiceRevenueDeltaPct { get; set; }

    public decimal PendingPayments { get; set; }
    public double? PendingPaymentsDeltaPct { get; set; }

    public decimal TotalTax { get; set; }
    public double? TotalTaxDeltaPct { get; set; }

    public decimal TotalServiceCharge { get; set; }
    public double? TotalServiceChargeDeltaPct { get; set; }

    public decimal NetProfit { get; set; }
    public double? NetProfitDeltaPct { get; set; }

    public List<AccountingTransaction> RecentTransactions { get; set; } = new();
    public List<Payment> RecentPayments { get; set; } = new();

    public ChartSeries RevenueAnalytics { get; set; } = new();
    public ChartSeries FinancialAnalytics { get; set; } = new();
    public ChartSeries TransactionAnalytics { get; set; } = new();
    public string Granularity { get; set; } = "month";
    public int? SelectedYear { get; set; }
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    public string SelectedRangeLabel { get; set; } = string.Empty;

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class ApprovalCenterViewModel
{
    public List<WorkflowInstance> Rows { get; set; } = new();
    public List<ServiceRequest> EscalatedRequests { get; set; } = new();

    public int TotalPending { get; set; }
    public int OverdueApprovals { get; set; }
    public int HighValueRequests { get; set; }
    public int EscalatedOperational { get; set; }

    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
}

public class ReportsViewModel
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }

    public decimal Revenue { get; set; }
    public decimal Income { get; set; }
    public decimal Expense { get; set; }
    public decimal NetProfit => Income - Expense;

    public int TotalReservations { get; set; }
    public int TotalServiceRequests { get; set; }

    // Analytics
    public ChartSeries RevenueAnalytics { get; set; } = new();
    public ChartSeries ReservationAnalytics { get; set; } = new();
    public ChartSeries ServiceAnalytics { get; set; } = new();
    public AnalyticsForecast Forecast { get; set; } = new();
}

public class ChartSeries
{
    public List<string> Labels { get; set; } = new();
    public Dictionary<string, List<decimal>> Datasets { get; set; } = new();
}

public class AnalyticsForecast
{
    public decimal ProjectedRevenue { get; set; }
    public int ProjectedReservations { get; set; }
    public decimal GrowthRate { get; set; }
}

public class AdminAuditLogsViewModel
{
    public DateOnly StartDate { get; set; }
    public DateOnly EndDate { get; set; }
    
    public int TotalLogsToday { get; set; }
    public int SecurityAlerts { get; set; }
    public int LoginAttempts { get; set; }
    public int CriticalEvents { get; set; }

    public List<AdminAuditLogRow> Rows { get; set; } = new();

    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);
    public string? Search { get; set; }
}

public class AdminAuditLogRow
{
    public int AuditId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string UserRole { get; set; } = string.Empty;
    public string Module { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string IpAddress { get; set; } = string.Empty;
    public string Status { get; set; } = "OK";
    public DateTime Timestamp { get; set; }
    public string? OldValues { get; set; }
    public string? NewValues { get; set; }
}

public class AdminNotificationsViewModel
{
    public List<Notification> Items { get; set; } = new();
    public int UnreadCount { get; set; }
}
