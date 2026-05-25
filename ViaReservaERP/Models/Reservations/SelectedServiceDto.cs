namespace ViaReservaERP.Models.Reservations;

public class SelectedServiceDto
{
    public int ServiceId { get; set; }
    public string Name { get; set; } = string.Empty;
    public decimal Price { get; set; }
    public int Quantity { get; set; } = 1;
}
