using Domain.Entities;

namespace Domain.Interfaces
{
    public record LlmAdrResult
    {
        public string Title { get; init; } = string.Empty;
        public string Content { get; init; } = string.Empty;
        public string Model { get; init; } = string.Empty;
        public int TokensUsed { get; init; }
    }

    public record LlmSpecResult
    {
        public string Title { get; init; } = string.Empty;

        /// <summary>
        /// Contenido estructurado generado por el LLM (JSON parseado).
        /// </summary>
        public Dictionary<string, object> Content { get; init; } = new();

        public string Model { get; init; } = string.Empty;
        public int TokensUsed { get; init; }

        /// <summary>
        /// Advertencia opcional (ej: "El cambio estimado supera 400 lineas").
        /// </summary>
        public string? Warning { get; init; }
    }

    public record LlmSectionResult
    {
        /// <summary>
        /// Contenido regenerado de la seccion.
        /// </summary>
        public object SectionContent { get; init; } = new();

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

        /// <summary>
        /// Genera un borrador de spec asistido por IA.
        /// ADR-P001: Opus para L1, Sonnet para L2/L3.
        /// </summary>
        Task<LlmSpecResult> GenerateSpecAsync(
            SpecLevel level,
            string systemPrompt,
            string userPrompt,
            CancellationToken ct = default);

        /// <summary>
        /// Regenera una seccion individual de una spec (ADR-P001: Sonnet).
        /// </summary>
        Task<LlmSectionResult> RegenerateSectionAsync(
            string systemPrompt,
            string userPrompt,
            CancellationToken ct = default);
    }
}
