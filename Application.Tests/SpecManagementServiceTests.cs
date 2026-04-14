using Application.Specs;
using Application.Specs.Exceptions;
using Application.Specs.Requests;
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using NSubstitute;
using NSubstitute.ExceptionExtensions;

namespace Application.Tests;

/// <summary>
/// Tests unitarios para SpecManagementService basados en GWT-01 a GWT-08 de SPEC-L2-create-specs.
/// </summary>
public class SpecManagementServiceTests
{
    private readonly ISpecRepository _specRepo;
    private readonly ISpecGenerationJobRepository _jobRepo;
    private readonly IProjectRepository _projectRepo;
    private readonly IAdrRepository _adrRepo;
    private readonly ILlmProvider _llmProvider;
    private readonly IDlpFilter _dlpFilter;
    private readonly ITemplateProvider _templateProvider;
    private readonly IEventPublisher _eventPublisher;
    private readonly IAuditLogRepository _auditRepo;
    private readonly ISpecGenerationQueue _generationQueue;
    private readonly SpecManagementService _sut;

    private static readonly Guid ProjectId = Guid.NewGuid();
    private static readonly Guid ActorId = Guid.NewGuid();
    private static readonly Guid CorrelationId = Guid.NewGuid();

    public SpecManagementServiceTests()
    {
        _specRepo = Substitute.For<ISpecRepository>();
        _jobRepo = Substitute.For<ISpecGenerationJobRepository>();
        _projectRepo = Substitute.For<IProjectRepository>();
        _adrRepo = Substitute.For<IAdrRepository>();
        _llmProvider = Substitute.For<ILlmProvider>();
        _dlpFilter = Substitute.For<IDlpFilter>();
        _templateProvider = Substitute.For<ITemplateProvider>();
        _eventPublisher = Substitute.For<IEventPublisher>();
        _auditRepo = Substitute.For<IAuditLogRepository>();
        _generationQueue = Substitute.For<ISpecGenerationQueue>();

        // DLP pass-through por defecto
        _dlpFilter.SanitizeInputAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(x.Arg<string>()));
        _dlpFilter.ValidateOutputAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(new DlpResult
            {
                SanitizedText = x.Arg<string>(),
                Findings = new List<string>()
            }));

        // Proyecto existe por defecto
        _projectRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(new Project { Id = ProjectId, Name = "Test Project", OwnerId = ActorId });

        _sut = new SpecManagementService(
            _specRepo, _jobRepo, _projectRepo, _adrRepo, _llmProvider,
            _dlpFilter, _templateProvider, _eventPublisher, _auditRepo,
            _generationQueue, Substitute.For<ILogger<SpecManagementService>>());
    }

    // ── GWT-01: Generar spec L1 exitosamente ─────────────────────────────

    [Fact]
    public async Task GenerateSpecL1_WithValidInput_ReturnsJobIdAndQueuesMessage()
    {
        // Given un proyecto existente y un usuario con rol SENIOR_DEV
        _jobRepo.CreateAsync(Arg.Any<SpecGenerationJob>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(x.Arg<SpecGenerationJob>()));

        var request = new GenerateSpecL1Request
        {
            ProjectId = ProjectId,
            Description = "Sistema de reservas de habitaciones con validación de disponibilidad"
        };

        // When solicita generar L1
        var result = await _sut.GenerateSpecL1Async(
            request, ActorId, UserRole.SENIOR_DEV, CorrelationId);

        // Then retorna 202 con jobId y encola en Service Bus
        Assert.NotEqual(Guid.Empty, result.JobId);
        Assert.Equal("QUEUED", result.Status);
        Assert.Equal(120, result.EstimatedTime); // L1 = 120s

        await _dlpFilter.Received(1).SanitizeInputAsync(
            request.Description, Arg.Any<CancellationToken>());
        await _jobRepo.Received(1).CreateAsync(
            Arg.Is<SpecGenerationJob>(j => j.Level == SpecLevel.L1 && j.ProjectId == ProjectId),
            Arg.Any<CancellationToken>());
        await _generationQueue.Received(1).EnqueueAsync(
            Arg.Any<SpecGenerationJob>(), Arg.Any<CancellationToken>());
    }

    // ── GWT-02: Generar spec L2 desde L1 aprobada ────────────────────────

    [Fact]
    public async Task GenerateSpecL2_WithApprovedL1Parent_ReturnsJobId()
    {
        // Given una spec L1 en estado APPROVED
        var parentL1 = new Specification
        {
            Id = Guid.NewGuid(),
            ProjectId = ProjectId,
            Level = SpecLevel.L1,
            Status = SpecStatus.APPROVED,
            Title = "Booking System",
            Content = new Dictionary<string, object> { ["objetivo"] = "reservas" }
        };

        _specRepo.GetByIdAsync(parentL1.Id, Arg.Any<CancellationToken>())
            .Returns(parentL1);
        _jobRepo.CreateAsync(Arg.Any<SpecGenerationJob>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(x.Arg<SpecGenerationJob>()));

        var request = new GenerateSpecL2Request
        {
            ProjectId = ProjectId,
            ParentSpecId = parentL1.Id,
            UseCaseId = "CU-01"
        };

        // When solicita derivar L2
        var result = await _sut.GenerateSpecL2Async(
            request, ActorId, UserRole.SENIOR_DEV, CorrelationId);

        // Then encola job L2
        Assert.Equal("QUEUED", result.Status);
        Assert.Equal(60, result.EstimatedTime); // L2 = 60s

        await _jobRepo.Received(1).CreateAsync(
            Arg.Is<SpecGenerationJob>(j =>
                j.Level == SpecLevel.L2 && j.ParentSpecId == parentL1.Id),
            Arg.Any<CancellationToken>());
    }

    // ── GWT-03: Rechazar L2 si parent no esta aprobado ───────────────────

    [Fact]
    public async Task GenerateSpecL2_WithDraftParent_ThrowsParentNotApproved()
    {
        // Given una spec L1 en estado DRAFT
        var draftL1 = new Specification
        {
            Id = Guid.NewGuid(),
            ProjectId = ProjectId,
            Level = SpecLevel.L1,
            Status = SpecStatus.DRAFT,
            Title = "Draft Spec"
        };

        _specRepo.GetByIdAsync(draftL1.Id, Arg.Any<CancellationToken>())
            .Returns(draftL1);

        var request = new GenerateSpecL2Request
        {
            ProjectId = ProjectId,
            ParentSpecId = draftL1.Id,
            UseCaseId = "CU-01"
        };

        // When intenta derivar L2 → Then retorna 422 PARENT_NOT_APPROVED
        var ex = await Assert.ThrowsAsync<ParentNotApprovedException>(
            () => _sut.GenerateSpecL2Async(
                request, ActorId, UserRole.SENIOR_DEV, CorrelationId));

        Assert.Equal(422, ex.HttpStatus);
        Assert.Equal("PARENT_NOT_APPROVED", ex.ErrorCode);
    }

    // ── GWT-03 variante: L2 con parent de nivel incorrecto ───────────────

    [Fact]
    public async Task GenerateSpecL2_WithL2Parent_ThrowsParentWrongLevel()
    {
        // Given una spec L2 (no L1)
        var wrongParent = new Specification
        {
            Id = Guid.NewGuid(),
            ProjectId = ProjectId,
            Level = SpecLevel.L2,
            Status = SpecStatus.APPROVED,
            Title = "Wrong Level"
        };

        _specRepo.GetByIdAsync(wrongParent.Id, Arg.Any<CancellationToken>())
            .Returns(wrongParent);

        var request = new GenerateSpecL2Request
        {
            ProjectId = ProjectId,
            ParentSpecId = wrongParent.Id,
            UseCaseId = "CU-01"
        };

        // Then retorna 422 PARENT_WRONG_LEVEL
        var ex = await Assert.ThrowsAsync<ParentWrongLevelException>(
            () => _sut.GenerateSpecL2Async(
                request, ActorId, UserRole.SENIOR_DEV, CorrelationId));

        Assert.Equal(422, ex.HttpStatus);
        Assert.Equal("PARENT_WRONG_LEVEL", ex.ErrorCode);
    }

    // ── GWT-03 variante: L3 con parent L2 no aprobado ────────────────────

    [Fact]
    public async Task GenerateSpecL3_WithNonApprovedL2_ThrowsParentNotApproved()
    {
        var parentL2 = new Specification
        {
            Id = Guid.NewGuid(),
            ProjectId = ProjectId,
            Level = SpecLevel.L2,
            Status = SpecStatus.IN_REVIEW,
            Title = "In Review"
        };

        _specRepo.GetByIdAsync(parentL2.Id, Arg.Any<CancellationToken>())
            .Returns(parentL2);

        var request = new GenerateSpecL3Request
        {
            ProjectId = ProjectId,
            ParentSpecId = parentL2.Id,
            ChangeDescription = "Agregar campo X"
        };

        var ex = await Assert.ThrowsAsync<ParentNotApprovedException>(
            () => _sut.GenerateSpecL3Async(
                request, ActorId, UserRole.SENIOR_DEV, CorrelationId));

        Assert.Equal(422, ex.HttpStatus);
    }

    // ── GWT-05: Manejar usuario no autorizado ────────────────────────────

    [Fact]
    public async Task GenerateSpecL1_WithQARole_ThrowsUnauthorized()
    {
        var request = new GenerateSpecL1Request
        {
            ProjectId = ProjectId,
            Description = "Test"
        };

        // QA no puede crear specs
        var ex = await Assert.ThrowsAsync<SpecUnauthorizedException>(
            () => _sut.GenerateSpecL1Async(
                request, ActorId, UserRole.QA, CorrelationId));

        Assert.Equal(403, ex.HttpStatus);
        Assert.Equal("UNAUTHORIZED", ex.ErrorCode);
    }

    [Fact]
    public async Task SaveSpecDraft_WithPORole_ThrowsUnauthorized()
    {
        var request = new SaveSpecDraftRequest
        {
            ProjectId = ProjectId,
            Level = SpecLevel.L1,
            Title = "Test",
            Content = new Dictionary<string, object> { ["objetivo"] = "test" },
            DataClassification = new Dictionary<string, object> { ["entidades"] = "User" }
        };

        var ex = await Assert.ThrowsAsync<SpecUnauthorizedException>(
            () => _sut.SaveSpecDraftAsync(
                request, ActorId, UserRole.PO, CorrelationId));

        Assert.Equal(403, ex.HttpStatus);
    }

    // ── GWT-08: Guardar spec manualmente sin IA ──────────────────────────

    [Fact]
    public async Task SaveSpecDraft_NewL1WithValidFields_CreatesSpec()
    {
        // Given un usuario con rol SENIOR_DEV
        _specRepo.ExistsDuplicateAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<SpecLevel>(), Arg.Any<CancellationToken>())
            .Returns(false);
        _specRepo.CreateAsync(Arg.Any<Specification>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(x.Arg<Specification>()));

        var request = new SaveSpecDraftRequest
        {
            ProjectId = ProjectId,
            Level = SpecLevel.L1,
            Title = "Sistema de reservas",
            Content = new Dictionary<string, object>
            {
                ["objetivo"] = "Gestionar reservas",
                ["entidades"] = new[] { "Reserva", "Habitacion" }
            },
            DataClassification = new Dictionary<string, object>
            {
                ["Reserva"] = "Confidencial"
            }
        };

        // When guarda como DRAFT
        var result = await _sut.SaveSpecDraftAsync(
            request, ActorId, UserRole.SENIOR_DEV, CorrelationId);

        // Then
        Assert.NotEqual(Guid.Empty, result.SpecId);
        Assert.Equal(SpecLevel.L1, result.Level);
        Assert.Equal(SpecStatus.DRAFT, result.Status);
        Assert.Equal("1.0.0", result.Version);
        Assert.Equal("Sistema de reservas", result.Title);

        await _specRepo.Received(1).CreateAsync(
            Arg.Is<Specification>(s => s.Version == "1.0.0" && s.Status == SpecStatus.DRAFT),
            Arg.Any<CancellationToken>());
        await _eventPublisher.Received(1).PublishAsync(
            Arg.Any<Domain.Events.SpecCreatedEvent>(), Arg.Any<CancellationToken>());
        await _auditRepo.Received(1).CreateAsync(
            Arg.Is<AuditLog>(a => a.Action == "SPEC_CREATED"),
            Arg.Any<CancellationToken>());
    }

    // ── Validación: L1 sin dataClassification ────────────────────────────

    [Fact]
    public async Task SaveSpecDraft_L1WithoutDataClassification_ThrowsMissingDataClassification()
    {
        var request = new SaveSpecDraftRequest
        {
            ProjectId = ProjectId,
            Level = SpecLevel.L1,
            Title = "Test Spec",
            Content = new Dictionary<string, object> { ["objetivo"] = "test" },
            DataClassification = null // Falta!
        };

        var ex = await Assert.ThrowsAsync<MissingDataClassificationException>(
            () => _sut.SaveSpecDraftAsync(
                request, ActorId, UserRole.SENIOR_DEV, CorrelationId));

        Assert.Equal(422, ex.HttpStatus);
        Assert.Equal("MISSING_DATA_CLASSIFICATION", ex.ErrorCode);
    }

    // ── Validación: L2 sin parentSpecId ──────────────────────────────────

    [Fact]
    public async Task SaveSpecDraft_L2WithoutParent_ThrowsIncompleteStructure()
    {
        var request = new SaveSpecDraftRequest
        {
            ProjectId = ProjectId,
            Level = SpecLevel.L2,
            ParentSpecId = null, // Falta!
            Title = "Test L2",
            Content = new Dictionary<string, object> { ["descripcion"] = "test" }
        };

        var ex = await Assert.ThrowsAsync<IncompleteStructureException>(
            () => _sut.SaveSpecDraftAsync(
                request, ActorId, UserRole.SENIOR_DEV, CorrelationId));

        Assert.Equal(422, ex.HttpStatus);
        Assert.Equal("INCOMPLETE_STRUCTURE", ex.ErrorCode);
    }

    // ── Validación: Spec duplicada ───────────────────────────────────────

    [Fact]
    public async Task SaveSpecDraft_DuplicateSpec_ThrowsDuplicateSpec()
    {
        _specRepo.ExistsDuplicateAsync(Arg.Any<Guid>(), Arg.Any<string>(), Arg.Any<SpecLevel>(), Arg.Any<CancellationToken>())
            .Returns(true);

        var request = new SaveSpecDraftRequest
        {
            ProjectId = ProjectId,
            Level = SpecLevel.L1,
            Title = "Duplicate Spec",
            Content = new Dictionary<string, object> { ["objetivo"] = "test" },
            DataClassification = new Dictionary<string, object> { ["e"] = "c" }
        };

        var ex = await Assert.ThrowsAsync<DuplicateSpecException>(
            () => _sut.SaveSpecDraftAsync(
                request, ActorId, UserRole.SENIOR_DEV, CorrelationId));

        Assert.Equal(409, ex.HttpStatus);
        Assert.Equal("DUPLICATE_SPEC", ex.ErrorCode);
    }

    // ── Validación: Proyecto no existe ───────────────────────────────────

    [Fact]
    public async Task GenerateSpecL1_ProjectNotFound_ThrowsProjectNotFound()
    {
        _projectRepo.GetByIdAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Project?)null);

        var request = new GenerateSpecL1Request
        {
            ProjectId = Guid.NewGuid(),
            Description = "Test"
        };

        var ex = await Assert.ThrowsAsync<ProjectNotFoundForSpecException>(
            () => _sut.GenerateSpecL1Async(
                request, ActorId, UserRole.ARCHITECT, CorrelationId));

        Assert.Equal(404, ex.HttpStatus);
        Assert.Equal("PROJECT_NOT_FOUND", ex.ErrorCode);
    }

    // ── GWT-06: Regenerar seccion individual ─────────────────────────────

    [Fact]
    public async Task RegenerateSection_ValidSection_UpdatesOnlyThatSection()
    {
        var spec = new Specification
        {
            Id = Guid.NewGuid(),
            ProjectId = ProjectId,
            Level = SpecLevel.L1,
            Status = SpecStatus.DRAFT,
            Title = "Test Spec",
            Content = new Dictionary<string, object>
            {
                ["objetivo"] = "original",
                ["casosDeUso"] = new[] { "CU-01" },
                ["entidades"] = new[] { "Entidad1" }
            },
            CreatedBy = ActorId
        };

        _specRepo.GetByIdAsync(spec.Id, Arg.Any<CancellationToken>()).Returns(spec);
        _templateProvider.GetValidSections(SpecLevel.L1)
            .Returns(new List<string> { "objetivo", "entidades", "reglas", "casosDeUso", "validaciones" });

        _llmProvider.RegenerateSectionAsync(
            Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(new LlmSectionResult
            {
                SectionContent = new[] { "CU-01", "CU-02-Cancelacion" },
                Model = "claude-sonnet-4-6",
                TokensUsed = 500
            });

        var request = new RegenerateSectionRequest
        {
            SpecId = spec.Id,
            Section = "casosDeUso",
            AdditionalInstructions = "Agregar caso de uso para cancelación"
        };

        var result = await _sut.RegenerateSectionAsync(
            request, ActorId, UserRole.SENIOR_DEV, CorrelationId);

        // Then
        Assert.Equal(spec.Id, result.SpecId);
        Assert.Equal("casosDeUso", result.Section);
        Assert.Equal("claude-sonnet-4-6", result.Model);
        Assert.Equal(500, result.TokensUsed);

        // Verify only the section was replaced, rest intact
        await _specRepo.Received(1).UpdateAsync(
            Arg.Is<Specification>(s =>
                s.Content.ContainsKey("objetivo") &&
                s.Content.ContainsKey("entidades") &&
                s.Content.ContainsKey("casosDeUso")),
            Arg.Any<CancellationToken>());

        await _auditRepo.Received(1).CreateAsync(
            Arg.Is<AuditLog>(a => a.Action == "SPEC_SECTION_REGENERATED"),
            Arg.Any<CancellationToken>());
    }

    // ── Validación: Sección no existe ────────────────────────────────────

    [Fact]
    public async Task RegenerateSection_InvalidSection_ThrowsSectionNotFound()
    {
        var spec = new Specification
        {
            Id = Guid.NewGuid(),
            ProjectId = ProjectId,
            Level = SpecLevel.L1,
            Content = new Dictionary<string, object> { ["objetivo"] = "test" }
        };

        _specRepo.GetByIdAsync(spec.Id, Arg.Any<CancellationToken>()).Returns(spec);
        _templateProvider.GetValidSections(SpecLevel.L1)
            .Returns(new List<string> { "objetivo", "entidades", "reglas", "casosDeUso", "validaciones" });

        var request = new RegenerateSectionRequest
        {
            SpecId = spec.Id,
            Section = "seccionInvalida"
        };

        var ex = await Assert.ThrowsAsync<SectionNotFoundException>(
            () => _sut.RegenerateSectionAsync(
                request, ActorId, UserRole.LEAD, CorrelationId));

        Assert.Equal(400, ex.HttpStatus);
        Assert.Equal("SECTION_NOT_FOUND", ex.ErrorCode);
    }

    // ── Upsert: actualizar spec existente ────────────────────────────────

    [Fact]
    public async Task SaveSpecDraft_WithExistingSpecId_UpdatesInsteadOfCreating()
    {
        var existingSpec = new Specification
        {
            Id = Guid.NewGuid(),
            ProjectId = ProjectId,
            Level = SpecLevel.L1,
            Title = "Old Title",
            Version = "1.0.0",
            Status = SpecStatus.DRAFT,
            Content = new Dictionary<string, object> { ["objetivo"] = "old" },
            DataClassification = new Dictionary<string, object> { ["e"] = "c" },
            CreatedBy = ActorId
        };

        _specRepo.GetByIdAsync(existingSpec.Id, Arg.Any<CancellationToken>())
            .Returns(existingSpec);

        var request = new SaveSpecDraftRequest
        {
            SpecId = existingSpec.Id,
            ProjectId = ProjectId,
            Level = SpecLevel.L1,
            Title = "Updated Title",
            Content = new Dictionary<string, object> { ["objetivo"] = "updated" },
            DataClassification = new Dictionary<string, object> { ["e"] = "c" }
        };

        var result = await _sut.SaveSpecDraftAsync(
            request, ActorId, UserRole.SENIOR_DEV, CorrelationId);

        Assert.Equal(existingSpec.Id, result.SpecId);

        // Update was called, not Create
        await _specRepo.Received(1).UpdateAsync(
            Arg.Is<Specification>(s => s.Title == "Updated Title"),
            Arg.Any<CancellationToken>());
        await _specRepo.DidNotReceive().CreateAsync(
            Arg.Any<Specification>(), Arg.Any<CancellationToken>());
    }

    // ── L1 DLP pre-prompt se invoca ──────────────────────────────────────

    [Fact]
    public async Task GenerateSpecL1_SanitizesInputViaDLP()
    {
        _jobRepo.CreateAsync(Arg.Any<SpecGenerationJob>(), Arg.Any<CancellationToken>())
            .Returns(x => Task.FromResult(x.Arg<SpecGenerationJob>()));

        _dlpFilter.SanitizeInputAsync("mi email es juan@empresa.com", Arg.Any<CancellationToken>())
            .Returns("mi email es [PII_REDACTED]");

        var request = new GenerateSpecL1Request
        {
            ProjectId = ProjectId,
            Description = "mi email es juan@empresa.com"
        };

        await _sut.GenerateSpecL1Async(
            request, ActorId, UserRole.ARCHITECT, CorrelationId);

        // Verify the job was created with sanitized input
        await _jobRepo.Received(1).CreateAsync(
            Arg.Is<SpecGenerationJob>(j => j.SanitizedInput == "mi email es [PII_REDACTED]"),
            Arg.Any<CancellationToken>());
    }

    // ── Parent not found ─────────────────────────────────────────────────

    [Fact]
    public async Task GenerateSpecL2_ParentNotFound_ThrowsParentNotFound()
    {
        var parentId = Guid.NewGuid();
        _specRepo.GetByIdAsync(parentId, Arg.Any<CancellationToken>())
            .Returns((Specification?)null);

        var request = new GenerateSpecL2Request
        {
            ProjectId = ProjectId,
            ParentSpecId = parentId,
            UseCaseId = "CU-01"
        };

        var ex = await Assert.ThrowsAsync<ParentNotFoundException>(
            () => _sut.GenerateSpecL2Async(
                request, ActorId, UserRole.SENIOR_DEV, CorrelationId));

        Assert.Equal(404, ex.HttpStatus);
        Assert.Equal("PARENT_NOT_FOUND", ex.ErrorCode);
    }
}
