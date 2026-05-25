namespace ViaReservaERP.Models.Reservations;

public class CreatePaymentIntentResponse
{
    public string ClientSecret { get; set; } = string.Empty;
    public string PaymentIntentId { get; set; } = string.Empty;
    public long Amount { get; set; }
    public string Currency { get; set; } = "php";
}
