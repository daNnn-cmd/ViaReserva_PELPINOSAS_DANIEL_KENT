using ViaReservaERP.Models;

namespace ViaReservaERP.Services;

public interface ICompanyService
{
    Task<List<Company>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<Company?> GetByIdAsync(int companyId, CancellationToken cancellationToken = default);
    Task<Company> CreateAsync(Company company, CancellationToken cancellationToken = default);
    Task<bool> UpdateAsync(Company company, CancellationToken cancellationToken = default);
    Task<bool> SoftDeleteAsync(int companyId, CancellationToken cancellationToken = default);
}
