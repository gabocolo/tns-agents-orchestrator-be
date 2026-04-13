using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Infrastructure.Ai
{
    public class AnthropicLlmProvider : ILlmProvider
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly ILogger<AnthropicLlmProvider> _logger;

        private const string ApiUrl = "https://api.anthropic.com/v1/messages";
        private const string Model = "claude-sonnet-4-6";
        private const string ApiVersion = "2023-06-01";
        private const int MaxTokens = 4096;

        public AnthropicLlmProvider(
            HttpClient httpClient,
            string apiKey,
            ILogger<AnthropicLlmProvider> logger)
        {
            _httpClient = httpClient;
            _apiKey = apiKey;
            _logger = logger;
        }

        public async Task<LlmAdrResult> GenerateAdrAsync(
            string description,
            string? context,
            CancellationToken ct = default)
        {
            var systemPrompt = BuildSystemPrompt();
            var userMessage = BuildUserMessage(description, context);

            var requestBody = new AnthropicRequest
            {
                Model = Model,
                MaxTokens = MaxTokens,
                System = systemPrompt,
                Messages = new List<AnthropicMessage>
                {
                    new() { Role = "user", Content = userMessage }
                }
            };

            var json = JsonSerializer.Serialize(requestBody, JsonOptions);

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
            request.Headers.Add("x-api-key", _apiKey);
            request.Headers.Add("anthropic-version", ApiVersion);
            request.Content = new StringContent(json, Encoding.UTF8, "application/json");

            _logger.LogInformation(
                "[AnthropicLlmProvider] Enviando solicitud al LLM. Model={Model} DescriptionLength={Length}",
                Model, description.Length);

            HttpResponseMessage response;
            try
            {
                response = await _httpClient.SendAsync(request, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AnthropicLlmProvider] Error de conexión con Anthropic API.");
                throw new InvalidOperationException($"No se pudo conectar con Anthropic API: {ex.Message}", ex);
            }

            var responseBody = await response.Content.ReadAsStringAsync(ct);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError(
                    "[AnthropicLlmProvider] Respuesta no exitosa del LLM. Status={Status} Body={Body}",
                    response.StatusCode, responseBody);
                throw new InvalidOperationException(
                    $"Anthropic API respondió con {(int)response.StatusCode}: {responseBody}");
            }

            var anthropicResponse = JsonSerializer.Deserialize<AnthropicResponse>(responseBody, JsonOptions);

            if (anthropicResponse == null || anthropicResponse.Content.Count == 0)
            {
                throw new InvalidOperationException("Respuesta vacía del LLM.");
            }

            var textContent = anthropicResponse.Content
                .FirstOrDefault(c => c.Type == "text")?.Text ?? string.Empty;

            var tokensUsed = (anthropicResponse.Usage?.InputTokens ?? 0)
                           + (anthropicResponse.Usage?.OutputTokens ?? 0);

            // Extraer título del contenido markdown (primera línea # Title)
            var title = ExtractTitle(textContent, description);

            _logger.LogInformation(
                "[AnthropicLlmProvider] ADR generado. Model={Model} Tokens={Tokens} TitleLength={TitleLength}",
                anthropicResponse.Model, tokensUsed, title.Length);

            return new LlmAdrResult
            {
                Title = title,
                Content = textContent,
                Model = anthropicResponse.Model ?? Model,
                TokensUsed = tokensUsed
            };
        }

        // ── Prompt construction ───────────────────────────────────────────────

        private static string BuildSystemPrompt()
        {
            return """
                Eres un arquitecto de software senior. Tu tarea es generar un ADR (Architecture Decision Record)
                completo en formato Markdown siguiendo la estructura estándar:

                # [Número]. [Título]

                ## Estado
                PROPOSED

                ## Contexto
                [Descripción del problema o necesidad]

                ## Decisión
                [Qué se decidió y por qué]

                ## Consecuencias
                [Impacto positivo y negativo de la decisión]

                ## Alternativas Consideradas
                [Otras opciones evaluadas]

                Responde SOLO con el contenido Markdown del ADR, sin explicaciones adicionales.
                No incluyas datos personales, credenciales ni información sensible.
                """;
        }

        private static string BuildUserMessage(string description, string? context)
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Genera un ADR para la siguiente decisión arquitectónica:");
            sb.AppendLine();
            sb.AppendLine($"**Descripción:** {description}");

            if (!string.IsNullOrWhiteSpace(context))
            {
                sb.AppendLine();
                sb.AppendLine($"**Contexto adicional:** {context}");
            }

            return sb.ToString();
        }

        private static string ExtractTitle(string markdownContent, string fallbackDescription)
        {
            // Intentar extraer el título del primer encabezado H1
            var lines = markdownContent.Split('\n');
            foreach (var line in lines)
            {
                var trimmed = line.Trim();
                if (trimmed.StartsWith("# "))
                {
                    var title = trimmed[2..].Trim();
                    // Quitar prefijo de número si existe (e.g., "001. Title" -> "Title")
                    var dotIndex = title.IndexOf(". ");
                    if (dotIndex >= 0 && dotIndex <= 5)
                        title = title[(dotIndex + 2)..].Trim();
                    return title;
                }
            }

            // Fallback: usar el inicio de la descripción
            return fallbackDescription.Length > 80
                ? fallbackDescription[..80] + "..."
                : fallbackDescription;
        }

        // ── Anthropic API DTOs ────────────────────────────────────────────────

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private sealed class AnthropicRequest
        {
            public string Model { get; init; } = string.Empty;

            [JsonPropertyName("max_tokens")]
            public int MaxTokens { get; init; }

            public string? System { get; init; }
            public List<AnthropicMessage> Messages { get; init; } = new();
        }

        private sealed class AnthropicMessage
        {
            public string Role { get; init; } = string.Empty;
            public string Content { get; init; } = string.Empty;
        }

        private sealed class AnthropicResponse
        {
            public string? Model { get; init; }
            public List<AnthropicContentBlock> Content { get; init; } = new();
            public AnthropicUsage? Usage { get; init; }
        }

        private sealed class AnthropicContentBlock
        {
            public string Type { get; init; } = string.Empty;
            public string? Text { get; init; }
        }

        private sealed class AnthropicUsage
        {
            [JsonPropertyName("input_tokens")]
            public int InputTokens { get; init; }

            [JsonPropertyName("output_tokens")]
            public int OutputTokens { get; init; }
        }
    }
}
