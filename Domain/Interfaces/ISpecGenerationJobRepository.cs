using Domain.Entities;

namespace Domain.Interfaces
{
    public interface ISpecGenerationJobRepository
    {
        Task<SpecGenerationJob?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<SpecGenerationJob> CreateAsync(SpecGenerationJob job, CancellationToken ct = default);
        Task UpdateAsync(SpecGenerationJob job, CancellationToken ct = default);
    }
}
