using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Threading.Channels;

namespace Infrastructure.Messaging
{
    /// <summary>
    /// Cola in-memory para MVP. En produccion se reemplaza por Azure Service Bus (ADR-P002).
    /// Implementa un Channel&lt;T&gt; para comunicacion entre API y Worker.
    /// </summary>
    public class InMemorySpecGenerationQueue : ISpecGenerationQueue
    {
        private readonly Channel<SpecGenerationJob> _channel;
        private readonly ILogger<InMemorySpecGenerationQueue> _logger;

        public InMemorySpecGenerationQueue(ILogger<InMemorySpecGenerationQueue> logger)
        {
            _channel = Channel.CreateBounded<SpecGenerationJob>(new BoundedChannelOptions(100)
            {
                FullMode = BoundedChannelFullMode.Wait
            });
            _logger = logger;
        }

        public async Task EnqueueAsync(SpecGenerationJob job, CancellationToken ct = default)
        {
            await _channel.Writer.WriteAsync(job, ct);

            _logger.LogInformation(
                "[SpecGenerationQueue] Job encolado. JobId={JobId} Level={Level}",
                job.Id, job.Level);
        }

        /// <summary>
        /// Lee jobs de la cola. Usado por el Worker para procesar generaciones.
        /// </summary>
        public ChannelReader<SpecGenerationJob> Reader => _channel.Reader;
    }
}
