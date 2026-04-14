using Application.Agents.ArchitectureAgent;
using Application.Agents.ProjectManagerAgent;
using Application.Agents.UnitTestAgent;
using Application.Governance;
using Application.Orchestration;
using Application.Proposals;
using Application.Shared;
using Application.Specs;
using Domain.Interfaces;
using Infrastructure.Ai;
using Infrastructure.Logging;
using Infrastructure.Messaging;
using Infrastructure.Persistence;
using Infrastructure.Plugins;
using Infrastructure.Settings;
using Infrastructure.Workers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.DependencyInjection
{
    public static class InfrastructureServiceExtensions
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            InfrastructureConfig config,
            KnowledgeBaseConfig? knowledgeBaseConfig = null)
        {
            // Config de Azure DevOps — compartida por los plugins
            var devOpsConfig = new AzureDevOpsConfig
            {
                OrganizationUrl = config.DevOpsOrganizationUrl,
                ProjectName = config.DevOpsProjectName,
                PersonalAccessToken = config.DevOpsPat
            };

            // Config del LLM
            var kernelConfig = new KernelConfig
            {
                ApiKey = config.OpenAiApiKey,
                DeploymentName = config.OpenAiModel   // ej: "gpt-4o"
            };

            // Plugins
            services.AddSingleton(devOpsConfig);
            services.AddSingleton<RepoReaderPlugin>();
            services.AddSingleton<DevOpsPlugin>();
            services.AddSingleton<CodeAnalyzerPlugin>();

            // Logger de decisiones
            services.AddSingleton<IDecisionLogger>(sp =>
                new SqlDecisionLogRepository(
                    config.SqlConnectionString,
                    sp.GetRequiredService<Microsoft.Extensions.Logging.ILogger<SqlDecisionLogRepository>>()
                ));

            // Interfaces de dominio → implementaciones de infra
            services.AddSingleton<IRepoReader>(sp => sp.GetRequiredService<RepoReaderPlugin>());
            services.AddSingleton<IDevOpsWriter>(sp => sp.GetRequiredService<DevOpsPlugin>());

            // Kernel config
            services.AddSingleton(kernelConfig);

            // Dispatcher
            services.AddSingleton<CoreDispatcher>();

            // Repositorios de conversación y estimaciones
            services.AddSingleton<IConversationRepository>(sp =>
                new ConversationRepository(
                    config.SqlConnectionString,
                    sp.GetRequiredService<ILogger<ConversationRepository>>()
                ));

            services.AddSingleton<IEstimationRepository>(sp =>
                new EstimationRepository(
                    config.SqlConnectionString,
                    sp.GetRequiredService<ILogger<EstimationRepository>>()
                ));

            services.AddSingleton<IAgentOutputHandler, ProjectEstimationOutputHandler>();
            services.AddSingleton<IAgentOutputHandler, ArchitectureIterationOutputHandler>();

            // ConversationService genérico — recibe todos los handlers automáticamente
            services.AddSingleton<ConversationService>();

            // Repositorio de propuestas
            services.AddSingleton<IProposalRepository>(sp =>
                new ProposalRepository(
                    config.SqlConnectionString,
                    sp.GetRequiredService<ILogger<ProposalRepository>>()
                ));

            // Extractor de métricas (fallback LLM para iteraciones sin métricas)
            services.AddSingleton<MetricsExtractor>();

            // Servicio de propuestas
            services.AddSingleton<IProposalService, ProposalService>();

            // Knowledge Base (Qdrant RAG) — requerido por ArchitectureAgentRunner
            if (knowledgeBaseConfig != null)
                services.AddKnowledgeBase(knowledgeBaseConfig, config.OpenAiApiKey);

            // Agentes — registrados como IAgentRunner para que el Dispatcher los encuentre
            services.AddSingleton<IAgentRunner, UnitTestAgentRunner>();
            services.AddSingleton<IAgentRunner, ProjectManagerAgentRunner>();
            services.AddSingleton<IAgentRunner, ArchitectureAgentRunner>();

            // ── Governance Service ────────────────────────────────────────────
            services.AddSingleton<IAuditLogRepository>(sp =>
                new GovernanceAuditLogRepository(
                    config.PostgresConnectionString,
                    sp.GetRequiredService<ILogger<GovernanceAuditLogRepository>>()
                ));

            services.AddSingleton<IProjectRepository>(sp =>
                new ProjectRepository(
                    config.PostgresConnectionString,
                    sp.GetRequiredService<ILogger<ProjectRepository>>()
                ));

            services.AddSingleton<IAdrRepository>(sp =>
                new AdrRepository(
                    config.PostgresConnectionString,
                    sp.GetRequiredService<ILogger<AdrRepository>>()
                ));

            services.AddSingleton<IQualityGateRepository>(sp =>
                new QualityGateRepository(
                    config.PostgresConnectionString,
                    sp.GetRequiredService<ILogger<QualityGateRepository>>()
                ));

            services.AddSingleton<IDlpFilter>(sp =>
                new RegexDlpFilter(
                    sp.GetRequiredService<ILogger<RegexDlpFilter>>()
                ));

            services.AddHttpClient("anthropic");

            services.AddSingleton<ILlmProvider>(sp =>
                new AnthropicLlmProvider(
                    sp.GetRequiredService<IHttpClientFactory>().CreateClient("anthropic"),
                    config.AnthropicApiKey,
                    sp.GetRequiredService<ILogger<AnthropicLlmProvider>>()
                ));

            services.AddSingleton<IGovernanceService, GovernanceService>();

            // ── Spec Management Service ──────────────────────────────────────
            services.AddSingleton<ISpecRepository>(sp =>
                new SpecRepository(
                    config.PostgresConnectionString,
                    sp.GetRequiredService<ILogger<SpecRepository>>()
                ));

            services.AddSingleton<ISpecGenerationJobRepository>(sp =>
                new SpecGenerationJobRepository(
                    config.PostgresConnectionString,
                    sp.GetRequiredService<ILogger<SpecGenerationJobRepository>>()
                ));

            services.AddSingleton<ITemplateProvider, SpecTemplateProvider>();

            services.AddSingleton<IEventPublisher>(sp =>
                new InMemoryEventPublisher(
                    sp.GetRequiredService<ILogger<InMemoryEventPublisher>>()
                ));

            // Cola de generación — in-memory para MVP (reemplazar por Azure Service Bus en prod)
            services.AddSingleton<InMemorySpecGenerationQueue>();
            services.AddSingleton<ISpecGenerationQueue>(sp =>
                sp.GetRequiredService<InMemorySpecGenerationQueue>());

            services.AddSingleton<ISpecManagementService, SpecManagementService>();

            // Worker de generación de specs (background service)
            services.AddHostedService<SpecGenerationWorker>();

            return services;
        }
    }
}
