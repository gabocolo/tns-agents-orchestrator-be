using Application.Specs.Exceptions;
using Application.Specs.Requests;
using Domain.Entities;
using Domain.Events;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Application.Specs
{
    public class SpecManagementService : ISpecManagementService
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
        private readonly ILogger<SpecManagementService> _logger;

        // Roles con permisos para crear specs (SPEC: ARCHITECT, LEAD, SENIOR_DEV)
        private static readonly HashSet<UserRole> SpecWriterRoles = new()
        {
            UserRole.ARCHITECT,
            UserRole.LEAD,
            UserRole.SENIOR_DEV
        };

        // Tiempo estimado por nivel (segundos)
        private static readonly Dictionary<SpecLevel, int> EstimatedTimes = new()
        {
            [SpecLevel.L1] = 120,
            [SpecLevel.L2] = 60,
            [SpecLevel.L3] = 60
        };

        private static readonly Regex SemverRegex = new(@"^\d+\.\d+\.\d+$", RegexOptions.Compiled);

        public SpecManagementService(
            ISpecRepository specRepo,
            ISpecGenerationJobRepository jobRepo,
            IProjectRepository projectRepo,
            IAdrRepository adrRepo,
            ILlmProvider llmProvider,
            IDlpFilter dlpFilter,
            ITemplateProvider templateProvider,
            IEventPublisher eventPublisher,
            IAuditLogRepository auditRepo,
            ISpecGenerationQueue generationQueue,
            ILogger<SpecManagementService> logger)
        {
            _specRepo = specRepo;
            _jobRepo = jobRepo;
            _projectRepo = projectRepo;
            _adrRepo = adrRepo;
            _llmProvider = llmProvider;
            _dlpFilter = dlpFilter;
            _templateProvider = templateProvider;
            _eventPublisher = eventPublisher;
            _auditRepo = auditRepo;
            _generationQueue = generationQueue;
            _logger = logger;
        }

        // ── GenerateSpecL1 ───────────────────────────────────────────────────

        public async Task<GenerationJobResponse> GenerateSpecL1Async(
            GenerateSpecL1Request request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default)
        {
            EnsureSpecPermission(actorRole);

            await EnsureProjectExistsAsync(request.ProjectId, ct);

            // DLP pre-prompt: sanitizar input del usuario (ADR-006)
            var sanitizedDescription = await _dlpFilter.SanitizeInputAsync(request.Description, ct);
            var sanitizedContext = request.AdditionalContext != null
                ? await _dlpFilter.SanitizeInputAsync(request.AdditionalContext, ct)
                : null;

            // Crear job de generacion asincrona (ADR-P002)
            var job = new SpecGenerationJob
            {
                Id = Guid.NewGuid(),
                ProjectId = request.ProjectId,
                Level = SpecLevel.L1,
                SanitizedInput = sanitizedDescription,
                AdditionalContext = sanitizedContext,
                Status = SpecJobStatus.QUEUED,
                CreatedBy = actorId,
                CorrelationId = correlationId
            };

            await _jobRepo.CreateAsync(job, ct);
            await _generationQueue.EnqueueAsync(job, ct);

            _logger.LogInformation(
                "[SpecManagementService] Job L1 encolado. JobId={JobId} ProjectId={ProjectId}",
                job.Id, request.ProjectId);

            return new GenerationJobResponse
            {
                JobId = job.Id,
                Status = "QUEUED",
                EstimatedTime = EstimatedTimes[SpecLevel.L1]
            };
        }

        // ── GenerateSpecL2 ───────────────────────────────────────────────────

        public async Task<GenerationJobResponse> GenerateSpecL2Async(
            GenerateSpecL2Request request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default)
        {
            EnsureSpecPermission(actorRole);

            await EnsureProjectExistsAsync(request.ProjectId, ct);

            // Validar parent L1
            var parent = await _specRepo.GetByIdAsync(request.ParentSpecId, ct)
                ?? throw new ParentNotFoundException(request.ParentSpecId);

            if (parent.Level != SpecLevel.L1)
                throw new ParentWrongLevelException("L1", parent.Level.ToString());

            if (parent.Status != SpecStatus.APPROVED)
                throw new ParentNotApprovedException(request.ParentSpecId);

            // DLP pre-prompt
            var sanitizedInput = await _dlpFilter.SanitizeInputAsync(
                $"UseCaseId: {request.UseCaseId}", ct);
            var sanitizedContext = request.TechnicalContext != null
                ? await _dlpFilter.SanitizeInputAsync(request.TechnicalContext, ct)
                : null;

            var job = new SpecGenerationJob
            {
                Id = Guid.NewGuid(),
                ProjectId = request.ProjectId,
                Level = SpecLevel.L2,
                ParentSpecId = request.ParentSpecId,
                SanitizedInput = sanitizedInput,
                AdditionalContext = sanitizedContext,
                Status = SpecJobStatus.QUEUED,
                CreatedBy = actorId,
                CorrelationId = correlationId
            };

            await _jobRepo.CreateAsync(job, ct);
            await _generationQueue.EnqueueAsync(job, ct);

            _logger.LogInformation(
                "[SpecManagementService] Job L2 encolado. JobId={JobId} ParentSpecId={ParentSpecId}",
                job.Id, request.ParentSpecId);

            return new GenerationJobResponse
            {
                JobId = job.Id,
                Status = "QUEUED",
                EstimatedTime = EstimatedTimes[SpecLevel.L2]
            };
        }

        // ── GenerateSpecL3 ───────────────────────────────────────────────────

        public async Task<GenerationJobResponse> GenerateSpecL3Async(
            GenerateSpecL3Request request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default)
        {
            EnsureSpecPermission(actorRole);

            await EnsureProjectExistsAsync(request.ProjectId, ct);

            // Validar parent L2
            var parent = await _specRepo.GetByIdAsync(request.ParentSpecId, ct)
                ?? throw new ParentNotFoundException(request.ParentSpecId);

            if (parent.Level != SpecLevel.L2)
                throw new ParentWrongLevelException("L2", parent.Level.ToString());

            if (parent.Status != SpecStatus.APPROVED)
                throw new ParentNotApprovedException(request.ParentSpecId);

            // DLP pre-prompt
            var sanitizedInput = await _dlpFilter.SanitizeInputAsync(request.ChangeDescription, ct);

            var job = new SpecGenerationJob
            {
                Id = Guid.NewGuid(),
                ProjectId = request.ProjectId,
                Level = SpecLevel.L3,
                ParentSpecId = request.ParentSpecId,
                SanitizedInput = sanitizedInput,
                Status = SpecJobStatus.QUEUED,
                CreatedBy = actorId,
                CorrelationId = correlationId
            };

            await _jobRepo.CreateAsync(job, ct);
            await _generationQueue.EnqueueAsync(job, ct);

            _logger.LogInformation(
                "[SpecManagementService] Job L3 encolado. JobId={JobId} ParentSpecId={ParentSpecId}",
                job.Id, request.ParentSpecId);

            return new GenerationJobResponse
            {
                JobId = job.Id,
                Status = "QUEUED",
                EstimatedTime = EstimatedTimes[SpecLevel.L3]
            };
        }

        // ── SaveSpecDraft ────────────────────────────────────────────────────

        public async Task<SpecDraftResponse> SaveSpecDraftAsync(
            SaveSpecDraftRequest request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default)
        {
            EnsureSpecPermission(actorRole);

            await EnsureProjectExistsAsync(request.ProjectId, ct);

            // Validar titulo
            if (string.IsNullOrWhiteSpace(request.Title) || request.Title.Length > 300)
                throw new IncompleteStructureException("title (obligatorio, max 300 chars)");

            // Validar content no vacio
            if (request.Content.Count == 0)
                throw new IncompleteStructureException("content (no puede estar vacío)");

            // Validar data classification para L1 (ADR-006)
            if (request.Level == SpecLevel.L1 && request.DataClassification == null)
                throw new MissingDataClassificationException();

            // Validar parent requerido para L2/L3
            if (request.Level != SpecLevel.L1 && request.ParentSpecId == null)
                throw new IncompleteStructureException("parentSpecId (requerido para L2/L3)");

            // Validar parent existe y es del nivel correcto
            if (request.ParentSpecId.HasValue)
            {
                var parent = await _specRepo.GetByIdAsync(request.ParentSpecId.Value, ct)
                    ?? throw new ParentNotFoundException(request.ParentSpecId.Value);

                var expectedParentLevel = request.Level == SpecLevel.L2 ? SpecLevel.L1 : SpecLevel.L2;
                if (parent.Level != expectedParentLevel)
                    throw new ParentWrongLevelException(expectedParentLevel.ToString(), parent.Level.ToString());
            }

            Specification spec;

            // Upsert: si specId viene, actualizar; si no, crear (ADR-002: idempotencia)
            if (request.SpecId.HasValue)
            {
                spec = await _specRepo.GetByIdAsync(request.SpecId.Value, ct)
                    ?? throw new SpecNotFoundException(request.SpecId.Value);

                spec.Title = request.Title;
                spec.Content = request.Content;
                spec.DataClassification = request.DataClassification;
                spec.UpdatedAt = DateTime.UtcNow;

                await _specRepo.UpdateAsync(spec, ct);
            }
            else
            {
                // Verificar duplicado
                if (await _specRepo.ExistsDuplicateAsync(request.ProjectId, request.Title, request.Level, ct))
                    throw new DuplicateSpecException(request.Title, request.Level.ToString());

                spec = new Specification
                {
                    Id = Guid.NewGuid(),
                    ProjectId = request.ProjectId,
                    ParentSpecId = request.ParentSpecId,
                    Level = request.Level,
                    Title = request.Title,
                    Version = "1.0.0",
                    Status = SpecStatus.DRAFT,
                    Content = request.Content,
                    DataClassification = request.DataClassification,
                    CreatedBy = actorId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                await _specRepo.CreateAsync(spec, ct);

                // Publicar evento SpecCreated
                await _eventPublisher.PublishAsync(new SpecCreatedEvent
                {
                    SpecId = spec.Id,
                    ProjectId = spec.ProjectId,
                    Level = spec.Level,
                    Title = spec.Title,
                    Version = spec.Version,
                    CreatedBy = actorId,
                    CreatedAt = spec.CreatedAt
                }, ct);
            }

            // Audit log
            await _auditRepo.CreateAsync(new AuditLog
            {
                ProjectId = spec.ProjectId,
                EntityType = nameof(Specification),
                EntityId = spec.Id,
                Action = AuditAction.SPEC_CREATED.ToString(),
                ActorId = actorId,
                CorrelationId = correlationId,
                Details = new Dictionary<string, object>
                {
                    ["title"] = spec.Title,
                    ["level"] = spec.Level.ToString(),
                    ["version"] = spec.Version
                }
            }, ct);

            _logger.LogInformation(
                "[SpecManagementService] Spec guardada. SpecId={SpecId} Level={Level} Title={Title}",
                spec.Id, spec.Level, spec.Title);

            return MapToResponse(spec);
        }

        // ── RegenerateSection ────────────────────────────────────────────────

        public async Task<RegenerateSectionResponse> RegenerateSectionAsync(
            RegenerateSectionRequest request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default)
        {
            EnsureSpecPermission(actorRole);

            var spec = await _specRepo.GetByIdAsync(request.SpecId, ct)
                ?? throw new SpecNotFoundException(request.SpecId);

            // Validar que la seccion existe en la spec
            var validSections = _templateProvider.GetValidSections(spec.Level);
            if (!validSections.Contains(request.Section))
                throw new SectionNotFoundException(request.Section);

            if (!spec.Content.ContainsKey(request.Section))
                throw new SectionNotFoundException(request.Section);

            // DLP pre-prompt
            var specContentJson = JsonSerializer.Serialize(spec.Content);
            var sanitizedContext = await _dlpFilter.SanitizeInputAsync(specContentJson, ct);
            var sanitizedInstructions = request.AdditionalInstructions != null
                ? await _dlpFilter.SanitizeInputAsync(request.AdditionalInstructions, ct)
                : null;

            // Construir prompts para regenerar solo la seccion
            var systemPrompt = $"""
                Eres un arquitecto de software senior. Regenera SOLO la sección "{request.Section}"
                de la siguiente especificación de nivel {spec.Level}.
                Responde UNICAMENTE con el contenido JSON de la sección, sin explicaciones.
                No incluyas datos personales, credenciales ni información sensible.
                """;

            var userPrompt = $"""
                ## Spec completa (contexto)
                {sanitizedContext}

                ## Sección a regenerar: {request.Section}

                ## Contenido actual de la sección
                {JsonSerializer.Serialize(spec.Content[request.Section])}
                {(sanitizedInstructions != null ? $"\n## Instrucciones adicionales\n{sanitizedInstructions}" : "")}
                """;

            LlmSectionResult llmResult;
            try
            {
                llmResult = await _llmProvider.RegenerateSectionAsync(systemPrompt, userPrompt, ct);
            }
            catch (Exception ex) when (ex is not SpecException)
            {
                _logger.LogError(ex,
                    "[SpecManagementService] Error al regenerar sección. SpecId={SpecId} Section={Section}",
                    request.SpecId, request.Section);
                throw new SpecLlmUnavailableException(ex.Message);
            }

            // DLP post-response
            var dlpResult = await _dlpFilter.ValidateOutputAsync(
                JsonSerializer.Serialize(llmResult.SectionContent), ct);

            if (dlpResult.Findings.Count > 0)
            {
                _logger.LogWarning(
                    "[SpecManagementService] DLP detectó PII en sección regenerada. Findings={Count} SpecId={SpecId}",
                    dlpResult.Findings.Count, request.SpecId);
            }

            // Reemplazar solo la seccion en el contenido, mantener el resto intacto
            var sanitizedSection = JsonSerializer.Deserialize<object>(dlpResult.SanitizedText);
            spec.Content[request.Section] = sanitizedSection!;
            spec.UpdatedAt = DateTime.UtcNow;

            await _specRepo.UpdateAsync(spec, ct);

            // Audit log
            await _auditRepo.CreateAsync(new AuditLog
            {
                ProjectId = spec.ProjectId,
                EntityType = nameof(Specification),
                EntityId = spec.Id,
                Action = AuditAction.SPEC_SECTION_REGENERATED.ToString(),
                ActorId = actorId,
                CorrelationId = correlationId,
                Details = new Dictionary<string, object>
                {
                    ["section"] = request.Section,
                    ["model"] = llmResult.Model,
                    ["tokensUsed"] = llmResult.TokensUsed
                }
            }, ct);

            _logger.LogInformation(
                "[SpecManagementService] Sección regenerada. SpecId={SpecId} Section={Section} Model={Model}",
                spec.Id, request.Section, llmResult.Model);

            return new RegenerateSectionResponse
            {
                SpecId = spec.Id,
                Section = request.Section,
                Model = llmResult.Model,
                TokensUsed = llmResult.TokensUsed,
                UpdatedAt = spec.UpdatedAt
            };
        }

        // ── GetJobStatus ─────────────────────────────────────────────────────

        public async Task<JobStatusResponse> GetJobStatusAsync(Guid jobId, CancellationToken ct = default)
        {
            var job = await _jobRepo.GetByIdAsync(jobId, ct)
                ?? throw new SpecNotFoundException(jobId);

            return new JobStatusResponse
            {
                JobId = job.Id,
                Status = job.Status.ToString(),
                ResultSpecId = job.ResultSpecId,
                Error = job.Error,
                CreatedAt = job.CreatedAt,
                CompletedAt = job.CompletedAt
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static void EnsureSpecPermission(UserRole role)
        {
            if (!SpecWriterRoles.Contains(role))
                throw new SpecUnauthorizedException();
        }

        private async Task EnsureProjectExistsAsync(Guid projectId, CancellationToken ct)
        {
            var project = await _projectRepo.GetByIdAsync(projectId, ct)
                ?? throw new ProjectNotFoundForSpecException(projectId);
        }

        private static SpecDraftResponse MapToResponse(Specification spec)
        {
            return new SpecDraftResponse
            {
                SpecId = spec.Id,
                ProjectId = spec.ProjectId,
                Level = spec.Level,
                ParentSpecId = spec.ParentSpecId,
                Version = spec.Version,
                Status = spec.Status,
                Title = spec.Title,
                Content = spec.Content,
                DataClassification = spec.DataClassification,
                Model = spec.Model,
                TokensUsed = spec.TokensUsed,
                CreatedBy = spec.CreatedBy,
                CreatedAt = spec.CreatedAt
            };
        }
    }
}
