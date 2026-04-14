using Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Infrastructure.Messaging
{
    public class InMemoryEventPublisher : IEventPublisher
    {
        private readonly ILogger<InMemoryEventPublisher> _logger;

        public InMemoryEventPublisher(ILogger<InMemoryEventPublisher> logger)
        {
            _logger = logger;
        }

        public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : class
        {
            _logger.LogInformation(
                "[EventPublisher] Evento publicado. Type={EventType} Event={@Event}",
                typeof(TEvent).Name, domainEvent);

            // MVP: log-only. En produccion, publicar a Azure Service Bus topic o evento de dominio.
            return Task.CompletedTask;
        }
    }
}
