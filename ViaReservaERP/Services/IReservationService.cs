using ViaReservaERP.Models;

namespace ViaReservaERP.Services;

public interface IReservationService
{
    Task<List<Reservation>> GetByCompanyAsync(int companyId, CancellationToken cancellationToken = default);
    Task<Reservation?> GetByIdAsync(int reservationId, CancellationToken cancellationToken = default);
    Task<Reservation> CreateAsync(Reservation reservation, CancellationToken cancellationToken = default);
    Task<bool> UpdateStatusAsync(int reservationId, string status, CancellationToken cancellationToken = default);
}
