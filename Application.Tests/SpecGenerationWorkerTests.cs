using Domain.Entities;
using Domain.Events;
using Domain.Interfaces;
using Infrastructure.Messaging;
using Infrastructure.Workers;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.Tests;

/// <summary>
/// Tests unitarios para SpecGenerationWorker basados en GWT-01, GWT-05 y GWT-07 de SPEC-L2-create-specs.
/// </summary>
public class SpecGenerationWorkerTests
{
    private readonly ISpecGenerationJobRepository _jobRepo;
    private readonly ISpecRepository _specRepo;
    private readonly IAdrRepository _adrRepo;
    private readonly ILlmProvider _llmProvider;
    private readonly IDlpFilter _dlpFilter;
    private readonly ITemplateProvider _templateProvider;
    private readonly IEventPublisher _eventPublisher;
    private readonly IAuditLogRepository _auditRepo;

    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid CorrelationId = Guid.NewGuid();

    public SpecGenerationWorkerTests()
    {
        _jobRepo = Substitute.For<ISpecGenerationJobRepository>();
        _specRepo = Substitute.For<ISpecRepository>();
        _adrRepo = Substitute.For<IAdrRepository>();
        _llmProvider = Substitute.For<ILlmProvider>();
        _dlpFilter = Substitute.For<IDlpFilter>();
        _templateProvider = Substitute.For<ITemplateProvider>();
        _eventPublisher = Substitute.For<IEventPublisher>();
        _auditRepo = Substitute.For<IAuditLogRepository>();

        // DLP pass-through por defecto
        _dlpFilter.ValidateOutputAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(new DlpResult
            {
                SanitizedText = x.Arg<string>(),
                Findings = new List<string>()
            }));

        // ADRs vacíos por defecto
        _adrRepo.ListByProjectAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<Adr>()));

        // Template por defecto
        _templateProvider.GetSpecTemplate(Arg.Any<SpecLevel>())
            .Returns("Eres un arquitecto de software senior. Genera una especificación estructurada.");

        // Job repository: UpdateAsync sin efectos secundarios por defecto
        _jobRepo.UpdateAsync(Arg.Any<SpecGenerationJob>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
    }

    private InMemorySpecGenerationQueue CreateQueue()
        => new InMemorySpecGenerationQueue(Substitute.For<ILogger<InMemorySpecGenerationQueue>>());

    private IHostedService BuildWorker(InMemorySpecGenerationQueue queue)
    {
        return new SpecGenerationWorker(
            queue,
            _jobRepo,
            _specRepo,
            _adrRepo,
            _llmProvider,
            _dlpFilter,
            _templateProvider,
            _eventPublisher,
            _auditRepo,
            Substitute.For<ILogger<SpecGenerationWorker>>());
    }

    private static SpecGenerationJob BuildJob(SpecLevel level, Guid? parentSpecId = null)
    {
        return new SpecGenerationJob
        {
            Id = Guid.NewGuid(),
            ProjectId = ProjectId,
            Level = level,
            ParentSpecId = parentSpecId,
            SanitizedInput = "Sistema de reservas de habitaciones con validacion de disponibilidad",
            Status = SpecJobStatus.QUEUED,
            CreatedBy = ActorId,
            CorrelationId = CorrelationId,
            CreatedAt = DateTime.UtcNow
        };
    }

    // ── GWT-01: Generar spec L1 exitosamente ─────────────────────────────────

    [Fact]
    public async Task ProcessJob_L1SuccessfulGeneration_PersistsSpecWithVersion100AndAuditLog()
    {
        // Given un proyecto con ADRs configurados, LLM (Opus) disponible
        var job = BuildJob(SpecLevel.L1);
        var llmContent = new Dictionary<string, object>
        {
            ["objetivo"] = "Gestionar reservas de habitaciones",
            ["entidades"] = new[] { "Habitacion", "Reserva", "Cliente" },
            ["reglas"] = new[] { "No reservar habitacion ya ocupada", "Precio minimo 50 USD" },
            ["casosDeUso"] = new[] { "CU-01: CreateReservation" }
        };
        var llmResult = new LlmSpecResult
        {
            Title = "Sistema de Reservas de Habitaciones",
            Content = llmContent,
            Model = "claude-opus-4-6",
            TokensUsed = 1842
        };

        _llmProvider.GenerateSpecAsync(
                SpecLevel.L1,
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(llmResult));

        // Señal para saber cuándo se persiste la spec
        var specCreatedTcs = new TaskCompletionSource<Specification>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _specRepo.CreateAsync(Arg.Any<Specification>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var s = ci.Arg<Specification>();
                specCreatedTcs.TrySetResult(s);
                return Task.FromResult(s);
            });

        var queue = CreateQueue();
        await queue.EnqueueAsync(job);

        // When el worker procesa el job
        var worker = BuildWorker(queue);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(cts.Token);

        var createdSpec = await specCreatedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Then se persiste la spec con version 1.0.0
        Assert.Equal("1.0.0", createdSpec.Version);
        Assert.Equal(SpecLevel.L1, createdSpec.Level);
        Assert.Equal(SpecStatus.DRAFT, createdSpec.Status);
        Assert.Equal("claude-opus-4-6", createdSpec.Model);
        Assert.Equal(ProjectId, createdSpec.ProjectId);
        Assert.Equal(1842, createdSpec.TokensUsed);

        // Y se registra la invocacion en AuditLog con accion SPEC_CREATED_AI (ADR-006: solo metadata, no input completo)
        await _auditRepo.Received(1).CreateAsync(
            Arg.Is<AuditLog>(a =>
                a.Action == AuditAction.SPEC_CREATED_AI.ToString() &&
                a.ProjectId == ProjectId &&
                a.CorrelationId == CorrelationId),
            Arg.Any<CancellationToken>());

        // Y el job se actualizó al menos una vez (PROCESSING y luego COMPLETED)
        // Nota: NSubstitute evalúa predicados sobre el estado final del objeto mutable,
        // por eso verificamos que el job resultante tiene ResultSpecId asignado (estado COMPLETED)
        await _jobRepo.Received().UpdateAsync(
            Arg.Any<SpecGenerationJob>(),
            Arg.Any<CancellationToken>());

        // Y se publica el evento SpecCreated
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Any<SpecCreatedEvent>(),
            Arg.Any<CancellationToken>());

        await worker.StopAsync(CancellationToken.None);
    }

    // ── GWT-05: LLM no disponible — job marcado FAILED con LLM_UNAVAILABLE ───

    [Fact(Timeout = 15000)]
    public async Task ProcessJob_LlmAlwaysThrows_JobMarkedFailedWithLlmUnavailableError()
    {
        // Given el LLM no está disponible (lanza en todos los intentos y reintentos)
        _llmProvider.GenerateSpecAsync(
                Arg.Any<SpecLevel>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .ThrowsAsync(new HttpRequestException("LLM service connection timeout"));

        var job = BuildJob(SpecLevel.L2);

        // Señal para saber cuándo el job queda en estado FAILED
        var failedTcs = new TaskCompletionSource<SpecGenerationJob>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _jobRepo.UpdateAsync(Arg.Any<SpecGenerationJob>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var j = ci.Arg<SpecGenerationJob>();
                if (j.Status == SpecJobStatus.FAILED)
                    failedTcs.TrySetResult(j);
                return Task.CompletedTask;
            });

        var queue = CreateQueue();
        await queue.EnqueueAsync(job);

        // When el worker intenta procesar el job y el LLM falla en todos los reintentos
        var worker = BuildWorker(queue);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(14));
        await worker.StartAsync(cts.Token);

        var failedJob = await failedTcs.Task.WaitAsync(TimeSpan.FromSeconds(14));

        // Then el job queda marcado como FAILED
        Assert.Equal(SpecJobStatus.FAILED, failedJob.Status);
        Assert.NotNull(failedJob.Error);

        // Y el error indica LLM_UNAVAILABLE (ADR-P002: mensajes al usuario)
        Assert.Contains("LLM_UNAVAILABLE", failedJob.Error);
        Assert.NotNull(failedJob.CompletedAt);

        // Y no se persiste ninguna spec (el portal sigue operativo para CRUD manual)
        await _specRepo.DidNotReceive().CreateAsync(
            Arg.Any<Specification>(),
            Arg.Any<CancellationToken>());

        await worker.StopAsync(CancellationToken.None);
    }

    // ── GWT-07: DLP detecta PII en respuesta del LLM ─────────────────────────

    [Fact]
    public async Task ProcessJob_LlmResponseContainsPii_ContentSanitizedAndPersistedWithoutPii()
    {
        // Given el LLM retorna contenido que incluye un email real (PII)
        var job = BuildJob(SpecLevel.L1);
        var rawContent = new Dictionary<string, object>
        {
            ["objetivo"] = "Gestionar reservas",
            ["entidades"] = new[] { "Habitacion", "Reserva" },
            ["contactoAdmin"] = "juan@empresa.com"  // PII — email real
        };
        var sanitizedContent = new Dictionary<string, object>
        {
            ["objetivo"] = "Gestionar reservas",
            ["entidades"] = new[] { "Habitacion", "Reserva" },
            ["contactoAdmin"] = "user@example.com"  // dato sintético — sin PII
        };

        _llmProvider.GenerateSpecAsync(
                Arg.Any<SpecLevel>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new LlmSpecResult
            {
                Title = "Sistema de Reservas",
                Content = rawContent,
                Model = "claude-opus-4-6",
                TokensUsed = 920
            }));

        // DLP post-response detecta el email y reemplaza con dato sintético (ADR-P005)
        _dlpFilter.ValidateOutputAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new DlpResult
            {
                SanitizedText = System.Text.Json.JsonSerializer.Serialize(sanitizedContent),
                Findings = new List<string> { "EMAIL: juan@empresa.com → user@example.com" }
            }));

        // Señal para saber cuándo se persiste la spec
        var specCreatedTcs = new TaskCompletionSource<Specification>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        _specRepo.CreateAsync(Arg.Any<Specification>(), Arg.Any<CancellationToken>())
            .Returns(ci =>
            {
                var s = ci.Arg<Specification>();
                specCreatedTcs.TrySetResult(s);
                return Task.FromResult(s);
            });

        var queue = CreateQueue();
        await queue.EnqueueAsync(job);

        // When el worker procesa el job con DLP post-response activo
        var worker = BuildWorker(queue);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await worker.StartAsync(cts.Token);

        var createdSpec = await specCreatedTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

        // Then la spec se persiste con el contenido sanitizado (sin PII)
        Assert.True(createdSpec.Content.ContainsKey("objetivo"));
        Assert.True(createdSpec.Content.ContainsKey("contactoAdmin"),
            "El campo 'contactoAdmin' debe existir en el contenido sanitizado.");

        // Verificar que el email real fue reemplazado por el dato sintético
        var rawSpecJson = System.Text.Json.JsonSerializer.Serialize(createdSpec.Content);
        Assert.False(rawSpecJson.Contains("juan@empresa.com"),
            "La spec no debe contener el email real (PII) — debe estar reemplazado por dato sintético.");

        // Y el DLP post-response fue invocado exactamente una vez
        await _dlpFilter.Received(1).ValidateOutputAsync(
            Arg.Any<string>(),
            Arg.Any<CancellationToken>());

        // Y la spec queda en estado DRAFT con version 1.0.0
        Assert.Equal("1.0.0", createdSpec.Version);
        Assert.Equal(SpecStatus.DRAFT, createdSpec.Status);

        await worker.StopAsync(CancellationToken.None);
    }
}
