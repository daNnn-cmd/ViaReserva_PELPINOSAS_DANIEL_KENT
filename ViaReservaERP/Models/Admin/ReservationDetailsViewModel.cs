using System.Collections.Generic;
using ViaReservaERP.Models;

namespace ViaReservaERP.Models.Admin;

public class ReservationDetailsViewModel
{
    public Reservation Reservation { get; set; } = null!;
    public WorkflowInstance? WorkflowInstance { get; set; }
    public List<WorkflowInstanceStep> WorkflowHistory { get; set; } = new();
    public bool CanApprove { get; set; }
    public Payment? Payment { get; set; }
}
