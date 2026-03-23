using Application.Proposals;
using Domain.Interfaces;
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

builder.Services.AddSingleton<IProposalRepository>(sp =>
    new ProposalRepository(
        sqlConnectionString,
        sp.GetRequiredService<ILogger<ProposalRepository>>()
    ));

builder.Services.AddSingleton<IProposalService, ProposalService>();

// ── Knowledge Base (Qdrant ingestion) ────────────────────────────────────────
var kbConfig = builder.Configuration
    .GetSection("KnowledgeBase")
    .Get<KnowledgeBaseConfig>() ?? new KnowledgeBaseConfig();

var openAiApiKey = builder.Configuration["Infrastructure:OpenAiApiKey"]
    ?? throw new InvalidOperationException(
        "Configura 'Infrastructure:OpenAiApiKey' en appsettings.");

builder.Services.AddKnowledgeBase(kbConfig, openAiApiKey);

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
