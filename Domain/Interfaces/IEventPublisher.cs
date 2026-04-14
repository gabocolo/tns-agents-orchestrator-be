namespace Domain.Interfaces
{
    public interface IEventPublisher
    {
        /// <summary>
        /// Publica un evento de dominio. El tipo del evento determina el topic/channel.
        /// </summary>
        Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct = default) where TEvent : class;
    }
}
