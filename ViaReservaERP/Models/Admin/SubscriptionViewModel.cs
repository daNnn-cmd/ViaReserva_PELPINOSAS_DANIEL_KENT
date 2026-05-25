using ViaReservaERP.Models;

namespace ViaReservaERP.Models.Admin;

public class SubscriptionViewModel
{
    public Company Company { get; set; } = null!;
    public ViaReservaERP.Models.Subscription? Subscription { get; set; }
    public bool HasStripeCustomer => !string.IsNullOrWhiteSpace(Company.StripeCustomerId);
}
