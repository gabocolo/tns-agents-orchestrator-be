using Domain.Entities;

namespace Domain.Interfaces
{
    public interface IProjectRepository
    {
        Task<bool> ExistsByNameAsync(string name, CancellationToken ct = default);
        Task<Project?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<Project> CreateAsync(Project project, CancellationToken ct = default);
        Task<List<Adr>> GetAdrsByProjectIdAsync(Guid projectId, CancellationToken ct = default);
        Task<List<QualityGateCheck>> GetGatesByProjectIdAsync(Guid projectId, CancellationToken ct = default);
    }
}
