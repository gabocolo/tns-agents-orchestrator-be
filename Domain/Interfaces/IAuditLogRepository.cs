using Domain.Entities;

namespace Domain.Interfaces
{
    public interface IAuditLogRepository
    {
        Task CreateAsync(AuditLog log, CancellationToken ct = default);
    }
}
