using Application.Governance;
using Application.Proposals;
using Application.Shared;
using Domain.Interfaces;
using Infrastructure.Ai;
using Infrastructure.DependencyInjection;
using Infrastructure.Persistence;
using Infrastructure.Settings;

var builder = WebApplication.CreateBuilder(args);

// ── CORS ───
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? Array.Empty<string>();

builder.Services.AddCors(options =>
{
    options.AddPolicy("AngularPortal", policy =>
    {
        policy
            .WithOrigins(allowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// Add services to the container.

// Lee la connection string del mismo bloque de configuración que EntryPoints.Web
var sqlConnectionString = builder.Configuration["Infrastructure:SqlConnectionString"]
    ?? builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException(
        "Configura 'Infrastructure:SqlConnectionString' en appsettings.");

var openAiApiKey = builder.Configuration["Infrastructure:OpenAiApiKey"]
    ?? throw new InvalidOperationException(
        "Configura 'Infrastructure:OpenAiApiKey' en appsettings.");

var pgConnStr = builder.Configuration["Infrastructure:PostgresConnectionString"] ?? string.Empty;
var anthropicKey = builder.Configuration["Infrastructure:AnthropicApiKey"] ?? string.Empty;

builder.Services.AddSingleton<IProposalRepository>(sp =>
    new ProposalRepository(
        sqlConnectionString,
        sp.GetRequiredService<ILogger<ProposalRepository>>()
    ));

// KernelConfig + MetricsExtractor para el fallback de extracción de métricas vía LLM
var openAiModel = builder.Configuration["Infrastructure:OpenAiModel"] ?? "gpt-4o-mini";
builder.Services.AddSingleton(new KernelConfig
{
    ApiKey = openAiApiKey,
    DeploymentName = openAiModel
});
builder.Services.AddSingleton<MetricsExtractor>();

builder.Services.AddSingleton<IProposalService, ProposalService>();

// ── Knowledge Base (Qdrant ingestion) ────────────────────────────────────────
var kbConfig = builder.Configuration
    .GetSection("KnowledgeBase")
    .Get<KnowledgeBaseConfig>() ?? new KnowledgeBaseConfig();

builder.Services.AddKnowledgeBase(kbConfig, openAiApiKey);

// ── Governance Service ────────────────────────────────────────────────────────
builder.Services.AddSingleton<IAuditLogRepository>(sp =>
    new GovernanceAuditLogRepository(
        pgConnStr,
        sp.GetRequiredService<ILogger<GovernanceAuditLogRepository>>()
    ));

builder.Services.AddSingleton<IProjectRepository>(sp =>
    new ProjectRepository(
        pgConnStr,
        sp.GetRequiredService<ILogger<ProjectRepository>>()
    ));

builder.Services.AddSingleton<IAdrRepository>(sp =>
    new AdrRepository(
        pgConnStr,
        sp.GetRequiredService<ILogger<AdrRepository>>()
    ));

builder.Services.AddSingleton<IQualityGateRepository>(sp =>
    new QualityGateRepository(
        pgConnStr,
        sp.GetRequiredService<ILogger<QualityGateRepository>>()
    ));

builder.Services.AddSingleton<IDlpFilter>(sp =>
    new RegexDlpFilter(
        sp.GetRequiredService<ILogger<RegexDlpFilter>>()
    ));

builder.Services.AddHttpClient();

builder.Services.AddSingleton<ILlmProvider>(sp =>
    new AnthropicLlmProvider(
        sp.GetRequiredService<IHttpClientFactory>().CreateClient("anthropic"),
        anthropicKey,
        sp.GetRequiredService<ILogger<AnthropicLlmProvider>>()
    ));

builder.Services.AddSingleton<IGovernanceService, GovernanceService>();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

// Usa el CORS antes del routing
app.UseCors("AngularPortal");

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();
