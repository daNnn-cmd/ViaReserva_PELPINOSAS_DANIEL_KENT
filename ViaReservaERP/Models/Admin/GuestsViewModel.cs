using System;
using System.Collections.Generic;
using ViaReservaERP.Models;

namespace ViaReservaERP.Models.Admin;

public class GuestsViewModel
{
    public List<Guest> Rows { get; set; } = new();
    
    public int TotalGuests { get; set; }
    public int ActiveGuests { get; set; }
    public int InactiveGuests { get; set; }
    public int NewGuestsThisMonth { get; set; }

    public string? Search { get; set; }
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalRows { get; set; }
    public int TotalPages => (int)Math.Ceiling((double)TotalRows / PageSize);
    public string Sort { get; set; } = "created";
    public string Dir { get; set; } = "desc";
}
