using Domain.Entities;

namespace Domain.Interfaces
{
    public interface ISpecGenerationQueue
    {
        /// <summary>
        /// Encola un job de generacion de spec en Azure Service Bus (ADR-P002).
        /// </summary>
        Task EnqueueAsync(SpecGenerationJob job, CancellationToken ct = default);
    }
}
