using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using ViaReservaERP.Models;

namespace ViaReservaERP.Models.Admin;

public class AddServiceViewModel
{
    public int ReservationId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    
    public List<SelectListItem> AvailableServices { get; set; } = new();
    public int SelectedServiceId { get; set; }
    public string? Notes { get; set; }
    public int Quantity { get; set; } = 1;
}

public class ReservationServicesViewModel
{
    public Reservation Reservation { get; set; } = null!;
    public List<ServiceRequest> ActiveRequests { get; set; } = new();
    public List<ReservationService> ServiceCharges { get; set; } = new();
}

public class ServiceManagementViewModel
{
    public List<ServiceCatalogItem> CatalogRows { get; set; } = new();
    public List<ServiceRequest> RequestRows { get; set; } = new();
    public List<SelectListItem> StaffOptions { get; set; } = new();
    public Dictionary<int, string> CurrentRoomByGuestId { get; set; } = new();

    public string? StatusFilter { get; set; }
    public string? Search { get; set; }

    // Stats
    public int TotalCatalogItems { get; set; }
    public int PendingRequests { get; set; }
    public int OverdueRequests { get; set; }
    public int CompletedToday { get; set; }

    // Pagination - Catalog
    public int CatalogPage { get; set; } = 1;
    public int CatalogPageSize { get; set; } = 10;
    public int CatalogTotalRows { get; set; }
    public int CatalogTotalPages => (int)Math.Ceiling((double)CatalogTotalRows / CatalogPageSize);

    // Pagination - Requests
    public int RequestPage { get; set; } = 1;
    public int RequestPageSize { get; set; } = 10;
    public int RequestTotalRows { get; set; }
    public int RequestTotalPages => (int)Math.Ceiling((double)RequestTotalRows / RequestPageSize);
}

public class ServiceCatalogFormViewModel
{
    public int? ServiceId { get; set; }

    [Required]
    [MaxLength(150)]
    public string ServiceName { get; set; } = string.Empty;

    [Range(0, 999999999)]
    public decimal Price { get; set; }
}
