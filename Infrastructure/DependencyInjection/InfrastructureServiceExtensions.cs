using Application.Agents.ArchitectureAgent;
using Application.Agents.ProjectManagerAgent;
using Application.Agents.UnitTestAgent;
using Application.Orchestration;
using Application.Proposals;
using Application.Shared;
using Domain.Interfaces;
using Infrastructure.Logging;
using Infrastructure.Persistence;
using Infrastructure.Plugins;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Infrastructure.DependencyInjection
{
    public static class InfrastructureServiceExtensions
    {
        public static IServiceCollection AddInfrastructure(
            this IServiceCollection services,
            InfrastructureConfig config)
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

            // Servicio de propuestas
            services.AddSingleton<IProposalService, ProposalService>();

            // Agentes — registrados como IAgentRunner para que el Dispatcher los encuentre
            services.AddSingleton<IAgentRunner, UnitTestAgentRunner>();
            services.AddSingleton<IAgentRunner, ProjectManagerAgentRunner>();
            services.AddSingleton<IAgentRunner, ArchitectureAgentRunner>();

            return services;
        }
    }
}
