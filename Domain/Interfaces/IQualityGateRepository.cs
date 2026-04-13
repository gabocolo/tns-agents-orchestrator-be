using Domain.Entities;

namespace Domain.Interfaces
{
    public interface IQualityGateRepository
    {
        /// <summary>
        /// Reemplaza todos los gates del proyecto en una transacción atómica.
        /// </summary>
        Task UpsertProjectGatesAsync(Guid projectId, List<QualityGateCheck> gates, CancellationToken ct = default);

        Task<List<QualityGateCheck>> ListByProjectAsync(Guid projectId, CancellationToken ct = default);
    }
}
