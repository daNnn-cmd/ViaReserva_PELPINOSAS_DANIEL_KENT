using System;
using System.Collections.Generic;
using ViaReservaERP.Models;

namespace ViaReservaERP.Models.Admin;

public class ReservationsViewModel
{
    public List<Reservation> Rows { get; set; } = new();
    
    // Filters
    public string? SearchGuest { get; set; }
    public string? StatusFilter { get; set; }
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate { get; set; }
    
    // Pagination & Sorting
    public string Sort { get; set; } = "id";
    public string Dir { get; set; } = "desc";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 20;
    public int TotalRows { get; set; }
    public int TotalPages => PageSize <= 0 ? 1 : (int)Math.Ceiling((double)TotalRows / PageSize);

    // Dashboard Stats (Today focus)
    public int TotalToday { get; set; }
    public int PendingCount { get; set; }
    public int CheckInsToday { get; set; }
    public int CheckOutsToday { get; set; }
    public int CancelledCount { get; set; }
    public decimal RevenueToday { get; set; }

    // Dropdowns for modals
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> AvailableServices { get; set; } = new();
    public List<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem> ActiveReservations { get; set; } = new();
}

