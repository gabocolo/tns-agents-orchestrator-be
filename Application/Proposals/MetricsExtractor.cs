using Application.Shared;
using Domain.Entities;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Text.Json;

namespace Application.Proposals
{
    /// <summary>
    /// Extrae métricas estructuradas de una propuesta de arquitectura usando el LLM.
    /// Se usa como fallback cuando el agente no incluyó el bloque json:metrics
    /// o cuando el frontend envía métricas vacías al guardar la iteración.
    /// </summary>
    public class MetricsExtractor
    {
        private readonly KernelConfig _kernelConfig;
        private readonly ILoggerFactory _loggerFactory;
        private readonly ILogger<MetricsExtractor> _logger;

        private const string ExtractionPrompt = """
            Dado el siguiente documento de propuesta de arquitectura, extrae las siguientes métricas en formato JSON estricto:

            - components: lista de nombres de componentes o servicios principales mencionados
            - teamSize: número de personas requeridas para el equipo
            - durationWeeks: duración estimada del proyecto en semanas
            - budgetUsd: presupuesto estimado en dólares USD
            - riskLevel: nivel de riesgo general, debe ser exactamente "low", "medium" o "high"

            Si algún dato no está explícito, infiere un valor razonable basado en la complejidad y alcance descritos.

            Responde ÚNICAMENTE con el JSON, sin markdown, sin explicaciones.

            Documento:
            {0}
            """;

        public MetricsExtractor(
            KernelConfig kernelConfig,
            ILoggerFactory loggerFactory)
        {
            _kernelConfig = kernelConfig;
            _loggerFactory = loggerFactory;
            _logger = loggerFactory.CreateLogger<MetricsExtractor>();
        }

        /// <summary>
        /// Llama al LLM para extraer métricas del contenido markdown de una iteración.
        /// Retorna true si logró extraer y aplicar las métricas a la iteración.
        /// </summary>
        public async Task<bool> ExtractAndApplyAsync(
            ProposalIteration iteration,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(iteration.Content))
                return false;

            try
            {
                var kernel = KernelFactory.Create(_kernelConfig, _loggerFactory);
                var chatService = kernel.GetRequiredService<IChatCompletionService>();

                var prompt = string.Format(ExtractionPrompt, iteration.Content);

                var settings = new OpenAIPromptExecutionSettings
                {
                    Temperature = 0.1,
                    MaxTokens = 500
                };

                var chatHistory = new ChatHistory();
                chatHistory.AddUserMessage(prompt);

                var response = await chatService.GetChatMessageContentAsync(
                    chatHistory, settings, kernel, ct);

                var rawJson = response.Content?.Trim() ?? string.Empty;

                // Limpiar posibles backticks que el LLM pueda incluir
                rawJson = rawJson
                    .Replace("```json", string.Empty)
                    .Replace("```", string.Empty)
                    .Trim();

                var metrics = JsonSerializer.Deserialize<ExtractedMetrics>(rawJson, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (metrics == null)
                    return false;

                // Aplicar métricas a la iteración
                if (metrics.Components?.Count > 0)
                    iteration.Components = metrics.Components;

                if (metrics.TeamSize > 0)
                    iteration.TeamSize = metrics.TeamSize;

                if (metrics.DurationWeeks > 0)
                    iteration.DurationWeeks = metrics.DurationWeeks;

                if (metrics.BudgetUsd > 0)
                    iteration.BudgetUsd = metrics.BudgetUsd;

                if (!string.IsNullOrEmpty(metrics.RiskLevel))
                {
                    if (Enum.TryParse<RiskLevel>(metrics.RiskLevel, true, out var parsed))
                        iteration.RiskLevel = parsed;
                }

                _logger.LogInformation(
                    "[MetricsExtractor] Métricas extraídas vía LLM. Components={Count} TeamSize={TeamSize} Weeks={Weeks}",
                    iteration.Components.Count, iteration.TeamSize, iteration.DurationWeeks);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[MetricsExtractor] Error extrayendo métricas vía LLM — se guardarán vacías");
                return false;
            }
        }

        private class ExtractedMetrics
        {
            public List<string>? Components { get; set; }
            public int TeamSize { get; set; }
            public int DurationWeeks { get; set; }
            public decimal BudgetUsd { get; set; }
            public string? RiskLevel { get; set; }
        }
    }
}
