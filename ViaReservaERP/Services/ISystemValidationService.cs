using ViaReservaERP.Models.SuperAdmin;

namespace ViaReservaERP.Services;

public interface ISystemValidationService
{
    Task<SystemValidationViewModel> RunAsync(CancellationToken ct = default);
    Task<SystemValidationViewModel> RunAndEscalateAsync(int performedByUserId, CancellationToken ct = default);
}
