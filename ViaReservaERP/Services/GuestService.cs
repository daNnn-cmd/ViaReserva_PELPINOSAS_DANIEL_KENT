using Microsoft.EntityFrameworkCore;
using ViaReservaERP.Data;
using ViaReservaERP.Models;

namespace ViaReservaERP.Services;

public class GuestService : IGuestService
{
    private readonly ViaReservaDbContext _db;

    public GuestService(ViaReservaDbContext db)
    {
        _db = db;
    }

    public Task<List<Guest>> GetByCompanyAsync(int companyId, CancellationToken cancellationToken = default)
    {
        return _db.Guests
            .Where(g => g.CompanyId == companyId)
            .OrderBy(g => g.FullName)
            .ToListAsync(cancellationToken);
    }

    public Task<Guest?> GetByIdAsync(int guestId, CancellationToken cancellationToken = default)
    {
        return _db.Guests
            .FirstOrDefaultAsync(g => g.GuestId == guestId, cancellationToken);
    }

    public async Task<Guest> CreateAsync(Guest guest, CancellationToken cancellationToken = default)
    {
        _db.Guests.Add(guest);
        await _db.SaveChangesAsync(cancellationToken);
        return guest;
    }

    public async Task<bool> UpdateAsync(Guest guest, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Guests
            .FirstOrDefaultAsync(g => g.GuestId == guest.GuestId, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        existing.CompanyId = guest.CompanyId;
        existing.FullName = guest.FullName;
        existing.Email = guest.Email;
        existing.Phone = guest.Phone;

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
