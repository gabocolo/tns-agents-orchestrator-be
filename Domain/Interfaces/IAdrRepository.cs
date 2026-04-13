using Domain.Entities;

namespace Domain.Interfaces
{
    public interface IAdrRepository
    {
        Task<bool> ExistsByNumberAsync(Guid projectId, int number, CancellationToken ct = default);
        Task<Adr?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<Adr> CreateAsync(Adr adr, CancellationToken ct = default);
        Task<List<Adr>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);
    }
}
