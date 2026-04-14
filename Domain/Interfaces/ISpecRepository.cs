using Domain.Entities;

namespace Domain.Interfaces
{
    public interface ISpecRepository
    {
        Task<Specification?> GetByIdAsync(Guid id, CancellationToken ct = default);
        Task<Specification> CreateAsync(Specification spec, CancellationToken ct = default);
        Task<Specification> UpdateAsync(Specification spec, CancellationToken ct = default);
        Task<List<Specification>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);

        /// <summary>
        /// Verifica si existe una spec con mismo titulo, nivel y status DRAFT o IN_REVIEW en el proyecto.
        /// </summary>
        Task<bool> ExistsDuplicateAsync(Guid projectId, string title, SpecLevel level, CancellationToken ct = default);

        /// <summary>
        /// Obtiene la version mas alta de una spec por titulo y nivel en un proyecto.
        /// </summary>
        Task<string?> GetLatestVersionAsync(Guid projectId, string title, SpecLevel level, CancellationToken ct = default);
    }
}
