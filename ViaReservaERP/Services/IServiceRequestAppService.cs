namespace ViaReservaERP.Services;

public interface IServiceRequestAppService
{
    Task UpdateStatusAsync(int requestId, string newStatus, int performedByUserId, CancellationToken ct = default);
    Task AssignAsync(int requestId, int assignedToUserId, int performedByUserId, CancellationToken ct = default);
}
