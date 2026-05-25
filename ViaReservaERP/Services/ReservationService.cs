using Microsoft.EntityFrameworkCore;
using ViaReservaERP.Data;
using ViaReservaERP.Models;

namespace ViaReservaERP.Services;

public class ReservationAppService : IReservationService
{
    private readonly ViaReservaDbContext _db;

    public ReservationAppService(ViaReservaDbContext db)
    {
        _db = db;
    }

    public Task<List<Reservation>> GetByCompanyAsync(int companyId, CancellationToken cancellationToken = default)
    {
        return _db.Reservations
            .Include(r => r.Guest)
            .Include(r => r.ReservationRooms)
                .ThenInclude(rr => rr.Room)
            .Where(r => r.CompanyId == companyId)
            .OrderByDescending(r => r.ReservationId)
            .ToListAsync(cancellationToken);
    }

    public Task<Reservation?> GetByIdAsync(int reservationId, CancellationToken cancellationToken = default)
    {
        return _db.Reservations
            .Include(r => r.Guest)
            .Include(r => r.ReservationRooms)
                .ThenInclude(rr => rr.Room)
            .Include(r => r.ReservationServices)
                .ThenInclude(rs => rs.Service)
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId, cancellationToken);
    }

    public async Task<Reservation> CreateAsync(Reservation reservation, CancellationToken cancellationToken = default)
    {
        _db.Reservations.Add(reservation);
        await _db.SaveChangesAsync(cancellationToken);
        return reservation;
    }

    public async Task<bool> UpdateStatusAsync(int reservationId, string status, CancellationToken cancellationToken = default)
    {
        var existing = await _db.Reservations
            .FirstOrDefaultAsync(r => r.ReservationId == reservationId, cancellationToken);

        if (existing is null)
        {
            return false;
        }

        existing.Status = status;
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
