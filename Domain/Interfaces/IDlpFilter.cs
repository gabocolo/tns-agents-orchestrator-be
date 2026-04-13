namespace Domain.Interfaces
{
    public record DlpResult
    {
        public string SanitizedText { get; init; } = string.Empty;
        public List<string> Findings { get; init; } = new();
    }

    public interface IDlpFilter
    {
        /// <summary>
        /// Pre-prompt: reemplaza PII con placeholders antes de enviar al LLM (ADR-P005).
        /// </summary>
        Task<string> SanitizeInputAsync(string text, CancellationToken ct = default);

        /// <summary>
        /// Post-response: detecta y reemplaza PII en la respuesta del LLM (ADR-P005).
        /// </summary>
        Task<DlpResult> ValidateOutputAsync(string text, CancellationToken ct = default);
    }
}
