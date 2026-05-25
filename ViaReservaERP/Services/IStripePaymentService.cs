using ViaReservaERP.Models.Reservations;

namespace ViaReservaERP.Services;

public interface IStripePaymentService
{
    Task<CreatePaymentIntentResponse> CreateReservationPaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken ct = default);
    Task<string?> GetPaymentIntentStatusAsync(string paymentIntentId, CancellationToken ct = default);
    Task<bool> RefundPaymentAsync(string paymentIntentId, CancellationToken ct = default);
    Task<string?> CreateCustomerPortalSessionAsync(string customerId, string returnUrl, CancellationToken ct = default);
}
