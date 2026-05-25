using ViaReservaERP.Models;

namespace ViaReservaERP.Services;

public interface IGuestService
{
    Task<List<Guest>> GetByCompanyAsync(int companyId, CancellationToken cancellationToken = default);
    Task<Guest?> GetByIdAsync(int guestId, CancellationToken cancellationToken = default);
    Task<Guest> CreateAsync(Guest guest, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Guest guest, CancellationToken cancellationToken = default);
}
