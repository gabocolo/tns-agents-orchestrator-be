using Domain.Interfaces;
using Infrastructure.Services;
using Infrastructure.Settings;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.SemanticKernel;
using Qdrant.Client;

namespace Infrastructure.DependencyInjection
{
    public static class KnowledgeBaseServiceExtensions
    {
        /// <summary>
        /// Registra todos los servicios necesarios para la base de conocimiento:
        /// QdrantClient, ITextEmbeddingGenerationService y IKnowledgeIngestionService.
        /// </summary>
        public static IServiceCollection AddKnowledgeBase(
            this IServiceCollection services,
            KnowledgeBaseConfig config,
            string openAiApiKey)
        {
            // Qdrant.Client is gRPC-only — always uses QdrantGrpcPort (default 6334)
            services.AddSingleton(new QdrantClient(config.QdrantHost, config.QdrantGrpcPort));

            // Config disponible para inyección directa en KnowledgeIngestionService
            services.AddSingleton(config);

            // ITextEmbeddingGenerationService via Semantic Kernel / OpenAI
            // Mismo proveedor que usan los agentes existentes
            var kernelBuilder = Kernel.CreateBuilder();
            kernelBuilder.AddOpenAITextEmbeddingGeneration(
                modelId: config.EmbeddingDeployment,
                apiKey: openAiApiKey);
            var kernel = kernelBuilder.Build();

            services.AddSingleton(
                kernel.GetRequiredService<Microsoft.SemanticKernel.Embeddings.ITextEmbeddingGenerationService>());

            // Singleton: todas sus dependencias (QdrantClient, embeddings, config) son singleton
            services.AddSingleton<IKnowledgeIngestionService, KnowledgeIngestionService>();

            return services;
        }
    }
}
