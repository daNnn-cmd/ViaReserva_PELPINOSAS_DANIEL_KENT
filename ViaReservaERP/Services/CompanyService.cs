using Microsoft.EntityFrameworkCore;
using ViaReservaERP.Data;
using ViaReservaERP.Models;

namespace ViaReservaERP.Services;

public class CompanyService : ICompanyService
{
    private readonly ViaReservaDbContext _db;

    public CompanyService(ViaReservaDbContext db)
    {
        _db = db;
    }

    public Task<List<Company>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        return _db.Companies
            .Where(c => !c.IsDeleted)
            .OrderBy(c => c.CompanyName)
            .ToListAsync(cancellationToken);
    }

    public Task<Company?> GetByIdAsync(int companyId, CancellationToken cancellationToken = default)
    {
        return _db.Companies
            .FirstOrDefaultAsync(c => c.CompanyId == companyId && !c.IsDeleted, cancellationToken);
    }

    public async Task<Company> CreateAsync(Company company, CancellationToken cancellationToken = default)
    {
        _db.Companies.Add(company);
        await _db.SaveChangesAsync(cancellationToken);
        return company;
    }

    public async Task<bool> UpdateAsync(Company company, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Companies
            .FirstOrDefaultAsync(c => c.CompanyId == company.CompanyId && !c.IsDeleted, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        existing.CompanyName = company.CompanyName;
        existing.Email = company.Email;
        existing.Phone = company.Phone;
        existing.Address = company.Address;
        existing.SubscriptionStatus = company.SubscriptionStatus;
        existing.IsActive = company.IsActive;
        existing.UpdatedAt = ViaReservaERP.AppTime.Now;
        existing.UpdatedBy = company.UpdatedBy;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<bool> SoftDeleteAsync(int companyId, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Companies
            .FirstOrDefaultAsync(c => c.CompanyId == companyId && !c.IsDeleted, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        existing.IsDeleted = true;
        existing.DeletedAt = ViaReservaERP.AppTime.Now;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
