using Infrastructure.DependencyInjection;
using Infrastructure.Settings;
using Serilog;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// ── Serilog ───
builder.Host.UseSerilog((context, services, config) =>
{
    config
        .ReadFrom.Configuration(context.Configuration)
        .WriteTo.Console(outputTemplate:
            "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}");
});

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

// ── Infrastructure (agentes, plugins, dispatcher) ────────────────────────────
var infraConfig = builder.Configuration
    .GetSection("Infrastructure")
    .Get<InfrastructureConfig>()
    ?? throw new InvalidOperationException(
        "Falta la sección 'Infrastructure' en appsettings.json");

var kbConfig = builder.Configuration
    .GetSection("KnowledgeBase")
    .Get<KnowledgeBaseConfig>();

builder.Services.AddInfrastructure(infraConfig, kbConfig);


// ── API ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// ── Pipeline ──────────────────────────────────────────────────────────────────
app.UseSerilogRequestLogging();
app.UseHttpsRedirection();
app.UseCors("AngularPortal");

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();
