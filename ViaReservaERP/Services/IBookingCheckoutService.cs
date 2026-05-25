using ViaReservaERP.Models.Reservations;

namespace ViaReservaERP.Services;

public interface IBookingCheckoutService
{
    Task<int> FinalizeReservationBookingAsync(BookReservationViewModel model, int? authenticatedUserId = null, CancellationToken ct = default);
    Task SyncRoomStatusesAsync(int? companyId = null, CancellationToken ct = default);
}
