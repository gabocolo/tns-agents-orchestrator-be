using Application.Orchestration;
using Domain.Interfaces;
using EntryPoints.Web.Mappers;
using EntryPoints.Web.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace EntryPoints.Web.Controllers
{
    [ApiController]
    [Route("api/agent")]
    //[Authorize]
    public class AgentStreamController : ControllerBase
    {
        private readonly CoreDispatcher _dispatcher;
        private readonly ILogger<AgentStreamController> _logger;

        // Marcadores de inicio de bloques ocultos — el FE no debe verlos
        private static readonly string[] HiddenBlockStarts =
        [
            "```json:metrics",
            "##REFERENCES_START##"
        ];

        public AgentStreamController(
            CoreDispatcher dispatcher,
            ILogger<AgentStreamController> logger)
        {
            _dispatcher = dispatcher;
            _logger = logger;
        }

        /// <summary>
        /// Endpoint SSE — devuelve la respuesta del agente token por token.
        /// Angular se conecta con EventSource y pinta el texto mientras llega.
        ///
        /// Bloques internos (```json:metrics y ##REFERENCES_START##) se ocultan
        /// del stream de tokens y se emiten como eventos SSE tipados al final.
        /// </summary>
        [HttpPost("stream")]
        public async Task StreamAsync(
            [FromBody] AgentHttpRequest request,
            CancellationToken ct)
        {
            if (!ModelState.IsValid)
            {
                Response.StatusCode = 400;
                return;
            }

            var userName = User.FindFirstValue(ClaimTypes.Name)
                        ?? User.FindFirstValue("preferred_username")
                        ?? "unknown";

            _logger.LogInformation(
                "[AgentStream] POST /stream Agent={Agent} User={User}",
                request.Agent, userName
            );

            // Configura los headers SSE
            Response.Headers["Content-Type"] = "text/event-stream";
            Response.Headers["Cache-Control"] = "no-cache";
            Response.Headers["Connection"] = "keep-alive";
            // Necesario para que Angular reciba los chunks en tiempo real
            Response.Headers["X-Accel-Buffering"] = "no";

            await Response.Body.FlushAsync(ct);

            try
            {
                var agentRequest = RequestNormalizer.ToAgentRequest(request, userName);
                var runner = _dispatcher.GetRunner(agentRequest.TargetAgent);

                // Acumula el response completo para extraer bloques especiales al finalizar
                var fullContent = new StringBuilder();

                if (runner is IStreamingAgentRunner streamingRunner)
                {
                    await StreamWithFilterAsync(streamingRunner, agentRequest, fullContent, ct);
                }
                else
                {
                    // Agente sin SSE — ejecuta normal y devuelve como un evento único
                    var result = await runner.RunAsync(agentRequest, ct);
                    fullContent.Append(result.Summary);
                    await WriteEventAsync(Response, "token", result.Summary, ct);
                }

                // Emite bloques especiales como eventos SSE tipados
                await EmitReferencesIfPresentAsync(fullContent.ToString(), Response, ct);
                await EmitMetricsIfPresentAsync(fullContent.ToString(), Response, ct);

                // Evento final — le dice al FE que el stream terminó
                await WriteEventAsync(Response, "done", "stream_complete", ct);
            }
            catch (ArgumentException ex)
            {
                await WriteEventAsync(Response, "error", ex.Message, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AgentStream] Error en stream");
                await WriteEventAsync(Response, "error", "Error procesando la solicitud.", ct);
            }
        }

        /// <summary>
        /// Procesa el stream token por token, ocultando bloques internos.
        ///
        /// Estrategia: usamos un "pending buffer" donde acumulamos tokens que
        /// PODRÍAN ser parte de un bloque oculto. Si confirmamos que no lo son,
        /// los flusheamos al FE. Si confirmamos que sí, los descartamos.
        ///
        /// Esto evita que el FE vea parcialmente "```json:met" antes de que
        /// podamos determinar si es un bloque a ocultar.
        /// </summary>
        private async Task StreamWithFilterAsync(
            IStreamingAgentRunner streamingRunner,
            Domain.Entities.AgentRequest agentRequest,
            StringBuilder fullContent,
            CancellationToken ct)
        {
            // pendingText acumula tokens que aún no sabemos si son parte de un bloque oculto
            var pendingText = new StringBuilder();
            bool insideHiddenBlock = false;

            await foreach (var token in streamingRunner.RunStreamingAsync(agentRequest, ct))
            {
                // Siempre acumular en fullContent (para extraer bloques al final)
                fullContent.Append(token);

                if (insideHiddenBlock)
                {
                    // Estamos dentro de un bloque oculto — acumular sin enviar
                    pendingText.Append(token);

                    // Detectar cierre del bloque
                    var pending = pendingText.ToString();

                    if (IsHiddenBlockClosed(pending))
                    {
                        // Bloque cerrado — descartar todo el contenido del bloque
                        insideHiddenBlock = false;
                        pendingText.Clear();
                    }

                    continue;
                }

                // No estamos en bloque oculto — acumular en pending
                pendingText.Append(token);
                var currentPending = pendingText.ToString();

                // Caso 1: El pending contiene el INICIO COMPLETO de un bloque oculto
                var hiddenStartIndex = FindHiddenBlockStart(currentPending);
                if (hiddenStartIndex >= 0)
                {
                    // Enviar todo lo que había ANTES del marcador
                    var beforeMarker = currentPending.Substring(0, hiddenStartIndex);
                    if (beforeMarker.Length > 0)
                    {
                        await WriteEventAsync(Response, "token", beforeMarker, ct);
                    }

                    // Entrar en modo oculto con lo que queda
                    insideHiddenBlock = true;
                    pendingText.Clear();
                    pendingText.Append(currentPending.Substring(hiddenStartIndex));

                    // Puede que el bloque ya se cerró en el mismo token
                    if (IsHiddenBlockClosed(pendingText.ToString()))
                    {
                        insideHiddenBlock = false;
                        pendingText.Clear();
                    }

                    continue;
                }

                // Caso 2: El pending PODRÍA ser el inicio parcial de un bloque oculto
                // Ej: el pending es "```json" que podría ser "```json:metrics"
                if (CouldBePartialHiddenStart(currentPending))
                {
                    // Mantener en pending — no enviar aún, esperar más tokens
                    continue;
                }

                // Caso 3: No hay nada especial — flush pending al FE
                await WriteEventAsync(Response, "token", currentPending, ct);
                pendingText.Clear();
            }

            // Si quedó algo en pending que no era bloque oculto, enviarlo
            if (pendingText.Length > 0 && !insideHiddenBlock)
            {
                await WriteEventAsync(Response, "token", pendingText.ToString(), ct);
            }
        }

        /// <summary>
        /// Busca si el texto contiene el inicio completo de algún bloque oculto.
        /// Retorna el índice donde empieza, o -1 si no lo tiene.
        /// </summary>
        private static int FindHiddenBlockStart(string text)
        {
            foreach (var marker in HiddenBlockStarts)
            {
                var idx = text.IndexOf(marker, StringComparison.Ordinal);
                if (idx >= 0) return idx;
            }
            return -1;
        }

        /// <summary>
        /// Verifica si el texto podría ser el inicio PARCIAL de un bloque oculto.
        /// Ej: "```j" podría ser parte de "```json:metrics" — no lo enviamos aún.
        ///
        /// Solo revisamos los últimos caracteres del texto acumulado, no todo.
        /// </summary>
        private static bool CouldBePartialHiddenStart(string text)
        {
            // Revisamos si el FINAL del texto podría ser el prefijo de algún marcador.
            // Tomamos los últimos N chars (donde N = largo del marcador más largo).
            var maxMarkerLen = 20; // "##REFERENCES_START##" tiene 20 chars
            var tail = text.Length <= maxMarkerLen
                ? text
                : text.Substring(text.Length - maxMarkerLen);

            foreach (var marker in HiddenBlockStarts)
            {
                // ¿Algún sufijo de tail es prefijo de marker?
                for (int len = 1; len < marker.Length && len <= tail.Length; len++)
                {
                    var suffix = tail.Substring(tail.Length - len);
                    var prefix = marker.Substring(0, len);
                    if (suffix == prefix) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// Determina si un bloque oculto ya se cerró.
        /// - Para ```json:metrics: busca el ``` de cierre (después del de apertura)
        /// - Para ##REFERENCES_START##: busca ##REFERENCES_END##
        /// </summary>
        private static bool IsHiddenBlockClosed(string buffered)
        {
            if (buffered.StartsWith("```json:metrics"))
            {
                // Buscar ``` de cierre — debe estar después de la apertura (pos 15+)
                var closeIdx = buffered.IndexOf("```", 15, StringComparison.Ordinal);
                return closeIdx >= 0;
            }

            if (buffered.StartsWith("##REFERENCES_START##"))
            {
                return buffered.Contains("##REFERENCES_END##");
            }

            return true; // Bloque desconocido — cerrar inmediatamente
        }

        private static async Task WriteEventAsync(
            HttpResponse response,
            string eventType,
            string data,
            CancellationToken ct)
        {
            // Formato SSE estándar:
            // event: token\n
            // data: el texto aquí\n\n
            var payload = $"event: {eventType}\ndata: {EscapeData(data)}\n\n";
            var bytes = Encoding.UTF8.GetBytes(payload);

            await response.Body.WriteAsync(bytes, ct);
            await response.Body.FlushAsync(ct);
        }

        /// <summary>
        /// Extrae el bloque ##REFERENCES_START## / ##REFERENCES_END## del contenido acumulado
        /// y lo emite como un evento SSE de tipo "references" con JSON compacto.
        /// </summary>
        private static async Task EmitReferencesIfPresentAsync(
            string fullContent,
            HttpResponse response,
            CancellationToken ct)
        {
            var match = Regex.Match(
                fullContent,
                @"##REFERENCES_START##\s*([\s\S]*?)\s*##REFERENCES_END##");

            if (!match.Success) return;

            // Colapsar a JSON de una sola línea — SSE no permite saltos de línea en data
            var refsJson = Regex.Replace(match.Groups[1].Value.Trim(), @"\s+", " ");
            await WriteEventAsync(response, "references", refsJson, ct);
        }

        /// <summary>
        /// Extrae el bloque ```json:metrics { ... } ``` del contenido acumulado
        /// y lo emite como un evento SSE de tipo "metrics" con JSON compacto.
        /// </summary>
        private static async Task EmitMetricsIfPresentAsync(
            string fullContent,
            HttpResponse response,
            CancellationToken ct)
        {
            var match = Regex.Match(
                fullContent,
                @"```json:metrics\s*(\{[\s\S]*?\})\s*```");

            if (!match.Success) return;

            try
            {
                // Validar que sea JSON válido y compactar en una línea
                var rawJson = match.Groups[1].Value.Trim();
                using var doc = JsonDocument.Parse(rawJson);
                var compactJson = JsonSerializer.Serialize(doc.RootElement);
                await WriteEventAsync(response, "metrics", compactJson, ct);
            }
            catch (JsonException)
            {
                // JSON inválido — no emitir evento, el fallback del FE se encargará
            }
        }

        // SSE no permite saltos de línea dentro del campo data
        // Los reemplazamos por el marcador estándar que el FE reconoce
        private static string EscapeData(string data)
            => data.Replace("\n", "\\n").Replace("\r", string.Empty);
    }
}
