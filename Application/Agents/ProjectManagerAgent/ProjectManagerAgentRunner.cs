using Application.Shared;
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.ComponentModel;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace Application.Agents.ProjectManagerAgent
{
    /// <summary>
    /// Agente especializado en estimación de proyectos.
    /// Toda la lógica de conversación multi-turno está en BaseConversationalAgentRunner.
    /// Este runner solo define: su prompt, cómo limpia el response y sus artifacts.
    ///
    /// Soporta RAG pre-retrieval: antes de cada respuesta del LLM, busca
    /// lineamientos organizacionales en Qdrant y los inyecta como contexto.
    /// </summary>
    public class ProjectManagerAgentRunner : BaseConversationalAgentRunner
    {
        public override AgentType AgentType => AgentType.ProjectManagerAgent;

        // Más temperatura que UnitTestAgent — necesita creatividad para estimar
        protected override double Temperature => 0.4;
        protected override int MaxTokens => 4000;

        private readonly IKnowledgeIngestionService _knowledgeService;

        public ProjectManagerAgentRunner(
            KernelConfig kernelConfig,
            ILoggerFactory loggerFactory,
            IDecisionLogger decisionLogger,
            ConversationService conversationService,
            IKnowledgeIngestionService knowledgeService)
            : base(
                kernelConfig,
                loggerFactory,
                decisionLogger,
                conversationService,
                loggerFactory.CreateLogger<ProjectManagerAgentRunner>())
        {
            _knowledgeService = knowledgeService;
        }

        // ─── Pre-retrieval RAG: siempre se ejecuta antes del LLM ─────────────

        protected override async Task<string?> FetchRagContextAsync(
            string userMessage, CancellationToken ct)
        {
            var result = await _knowledgeService.SearchAsync(userMessage, topK: 5);

            if (!result.Items.Any())
                return null;

            var sb = new StringBuilder();
            sb.AppendLine("=== LINEAMIENTOS ORGANIZACIONALES OBLIGATORIOS ===");
            sb.AppendLine("Los siguientes lineamientos aprobados por la organización son");
            sb.AppendLine("OBLIGATORIOS para esta respuesta. Tenlos en cuenta al estimar");
            sb.AppendLine("tecnologías, metodologías y prácticas del proyecto.");
            sb.AppendLine();

            foreach (var item in result.Items)
            {
                sb.AppendLine($"[FUENTE: {item.FileName} | RELEVANCIA: {item.Score:P0} | CATEGORÍA: {item.Category}]");
                sb.AppendLine(item.Content);
                sb.AppendLine("---");
            }

            sb.AppendLine("=== FIN LINEAMIENTOS ===");
            sb.AppendLine("Al final de tu respuesta incluye el bloque ##REFERENCES_START## con las fuentes usadas.");

            return sb.ToString();
        }

        // ─── Kernel: registrar este objeto como plugin (búsquedas adicionales) ──

        protected override void ConfigureKernel(Kernel kernel)
        {
            kernel.ImportPluginFromObject(this, "KnowledgeBasePlugin");
        }

        // ─── Habilitar auto-invocación de funciones ───────────────────────────

        protected override OpenAIPromptExecutionSettings BuildExecutionSettings()
            => new()
            {
                Temperature = Temperature,
                MaxTokens = MaxTokens,
                FunctionChoiceBehavior = FunctionChoiceBehavior.Auto()
            };

        // ─── KernelFunction: consulta RAG ─────────────────────────────────────

        [KernelFunction("search_knowledge_base")]
        [Description("Busca lineamientos y estándares de la organización " +
                     "relacionados con estimación, metodologías, tecnologías " +
                     "y prácticas aprobadas. Úsala cuando necesites validar " +
                     "decisiones contra los lineamientos organizacionales.")]
        public async Task<string> SearchKnowledgeBaseAsync(
            [Description("Query de búsqueda en español sobre el lineamiento necesario")]
            string query,
            Kernel kernel)
        {
            var result = await _knowledgeService.SearchAsync(query, topK: 4);

            if (!result.Items.Any())
                return "No se encontraron lineamientos específicos para esta consulta.";

            var sb = new StringBuilder();
            sb.AppendLine("=== LINEAMIENTOS ORGANIZACIONALES ENCONTRADOS ===");

            foreach (var item in result.Items)
            {
                sb.AppendLine($"[FUENTE: {item.FileName} | RELEVANCIA: {item.Score:P0}]");
                sb.AppendLine(item.Content);
                sb.AppendLine("---");
            }

            sb.AppendLine("=== FIN LINEAMIENTOS ===");
            sb.AppendLine("IMPORTANTE: Tenlos en cuenta en tu estimación. " +
                          "Al final de tu respuesta incluye el bloque ##REFERENCES_START## " +
                          "con las fuentes que usaste.");

            return sb.ToString();
        }

        // ─── System prompt ────────────────────────────────────────────────────

        protected override string BuildSystemPrompt(AgentRequest request, Guid sessionId)
        {
            var template = LoadPromptTemplate();
            return template
                .Replace("{{userName}}", request.UserName)
                .Replace("{{sessionId}}", sessionId.ToString());
        }

        // ─── Limpieza del response ────────────────────────────────────────────

        protected override string CleanResponse(string rawResponse)
        {
            var clean = rawResponse
                .Replace("```ESTIMATION_START```", string.Empty)
                .Replace("```ESTIMATION_END```", string.Empty)
                .Replace("ESTIMATION_START", string.Empty)
                .Replace("ESTIMATION_END", string.Empty);

            // Quitar bloque de referencias (ya fue emitido como evento SSE separado)
            clean = Regex.Replace(
                clean,
                @"##REFERENCES_START##[\s\S]*?##REFERENCES_END##",
                string.Empty,
                RegexOptions.None);

            return clean.Trim();
        }

        protected override List<AgentArtifact> BuildArtifacts(string rawResponse, Guid sessionId)
        {
            if (!rawResponse.Contains("ESTIMATION_START"))
                return new();

            return new List<AgentArtifact>
        {
            new()
            {
                Type  = "Estimation",
                Label = "Estimación completa generada",
                Value = sessionId.ToString()
            }
        };
        }

        private static string LoadPromptTemplate()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "Application.Agents.ProjectManagerAgent.Prompts.SystemPrompt.txt";

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new FileNotFoundException($"No se encontró: {resourceName}");

            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
