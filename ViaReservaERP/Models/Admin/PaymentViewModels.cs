using System.Collections.Generic;
using ViaReservaERP.Models;

namespace ViaReservaERP.Models.Admin;

public class PaymentViewModel
{
    public int ReservationId { get; set; }
    public string GuestName { get; set; } = string.Empty;
    public decimal TotalAmount { get; set; }
    public decimal AlreadyPaid { get; set; }
    public decimal RemainingBalance => TotalAmount - AlreadyPaid;
    
    // Form fields
    public decimal PaymentAmount { get; set; }
    public string PaymentMethod { get; set; } = "Cash"; // Cash, Card, Bank Transfer, Stripe
    public string PaymentType { get; set; } = "Full"; // Deposit, Partial, Full, Refund
    public string? Reference { get; set; }
}

public class InvoiceViewModel
{
    public Reservation Reservation { get; set; } = null!;
    public List<Payment> Payments { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public decimal TotalPaid { get; set; }
    public decimal Balance => TotalAmount - TotalPaid;
}
