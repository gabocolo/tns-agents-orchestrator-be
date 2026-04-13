namespace Domain.Interfaces
{
    public record LlmAdrResult
    {
        public string Title { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public int TokensUsed { get; init; }
    }

    public interface ILlmProvider
    {
        /// <summary>
        /// Genera un borrador de ADR asistido por IA (ADR-P001: usa claude-sonnet-4-6).
        /// Lanza LlmUnavailableException cuando el LLM no responde.
        /// </summary>
        Task<LlmAdrResult> GenerateAdrAsync(string description, string? context, CancellationToken ct = default);
    }
}
