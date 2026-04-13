using Application.Governance.Exceptions;
using Application.Governance.Requests;
using Domain.Entities;
using Domain.Interfaces;
using Microsoft.Extensions.Logging;

namespace Application.Governance
{
    public class GovernanceService : IGovernanceService
    {
        private readonly IProjectRepository _projectRepo;
        private readonly IAdrRepository _adrRepo;
        private readonly IQualityGateRepository _gateRepo;
        private readonly ILlmProvider _llmProvider;
        private readonly IDlpFilter _dlpFilter;
        private readonly IAuditLogRepository _auditRepo;
        private readonly ILogger<GovernanceService> _logger;

        // Roles con permisos de escritura en governance
        private static readonly HashSet<UserRole> WriterRoles = new()
        {
            UserRole.ARCHITECT,
            UserRole.LEAD
        };

        public GovernanceService(
            IProjectRepository projectRepo,
            IAdrRepository adrRepo,
            IQualityGateRepository gateRepo,
            ILlmProvider llmProvider,
            IDlpFilter dlpFilter,
            IAuditLogRepository auditRepo,
            ILogger<GovernanceService> logger)
        {
            _projectRepo = projectRepo;
            _adrRepo = adrRepo;
            _gateRepo = gateRepo;
            _llmProvider = llmProvider;
            _dlpFilter = dlpFilter;
            _auditRepo = auditRepo;
            _logger = logger;
        }

        // ── CreateProject ─────────────────────────────────────────────────────

        public async Task<CreateProjectResult> CreateProjectAsync(
            CreateProjectRequest request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default)
        {
            EnsureWritePermission(actorRole);

            if (string.IsNullOrWhiteSpace(request.Name))
                throw new GovernanceException(400, "BAD_REQUEST", "El nombre del proyecto es obligatorio.");

            if (await _projectRepo.ExistsByNameAsync(request.Name, ct))
                throw new ProjectAlreadyExistsException(request.Name);

            var project = new Project
            {
                Id = Guid.NewGuid(),
                Name = request.Name,
                Description = request.Description,
                GitRepoUrl = request.GitRepoUrl,
                OwnerId = actorId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _projectRepo.CreateAsync(project, ct);

            await _auditRepo.CreateAsync(new AuditLog
            {
                ProjectId = project.Id,
                EntityType = nameof(Project),
                EntityId = project.Id,
                Action = AuditAction.PROJECT_CREATED.ToString(),
                ActorId = actorId,
                CorrelationId = correlationId,
                Details = new Dictionary<string, object>
                {
                    ["name"] = project.Name
                }
            }, ct);

            _logger.LogInformation(
                "[GovernanceService] Proyecto creado. ProjectId={ProjectId} Name={Name} Actor={ActorId}",
                project.Id, project.Name, actorId);

            return new CreateProjectResult
            {
                ProjectId = project.Id,
                Name = project.Name,
                CreatedAt = project.CreatedAt
            };
        }

        // ── CreateAdr ─────────────────────────────────────────────────────────

        public async Task<CreateAdrResult> CreateAdrAsync(
            CreateAdrRequest request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default)
        {
            EnsureWritePermission(actorRole);

            var project = await _projectRepo.GetByIdAsync(request.ProjectId, ct)
                ?? throw new ProjectNotFoundException(request.ProjectId);

            if (await _adrRepo.ExistsByNumberAsync(request.ProjectId, request.Number, ct))
                throw new AdrNumberConflictException(request.ProjectId, request.Number);

            var adr = new Adr
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Number = request.Number,
                Title = request.Title,
                Status = request.Status,
                Content = request.Content,
                CreatedBy = actorId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _adrRepo.CreateAsync(adr, ct);

            await _auditRepo.CreateAsync(new AuditLog
            {
                ProjectId = project.Id,
                EntityType = nameof(Adr),
                EntityId = adr.Id,
                Action = AuditAction.ADR_CREATED.ToString(),
                ActorId = actorId,
                CorrelationId = correlationId,
                Details = new Dictionary<string, object>
                {
                    ["number"] = adr.Number,
                    ["title"] = adr.Title
                }
            }, ct);

            _logger.LogInformation(
                "[GovernanceService] ADR creado. AdrId={AdrId} Number={Number} ProjectId={ProjectId}",
                adr.Id, adr.Number, project.Id);

            return new CreateAdrResult
            {
                AdrId = adr.Id,
                ProjectId = project.Id,
                Number = adr.Number,
                Title = adr.Title,
                Status = adr.Status,
                CreatedAt = adr.CreatedAt
            };
        }

        // ── CreateAdrWithAi ──────────────────────────────────────────────────

        public async Task<CreateAdrWithAiResult> CreateAdrWithAiAsync(
            CreateAdrWithAiRequest request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default)
        {
            EnsureWritePermission(actorRole);

            var project = await _projectRepo.GetByIdAsync(request.ProjectId, ct)
                ?? throw new ProjectNotFoundException(request.ProjectId);

            // Pre-prompt DLP: sanitizar entradas antes de enviar al LLM (ADR-P005)
            var sanitizedDescription = await _dlpFilter.SanitizeInputAsync(request.Description, ct);
            var sanitizedContext = request.Context != null
                ? await _dlpFilter.SanitizeInputAsync(request.Context, ct)
                : null;

            LlmAdrResult llmResult;
            try
            {
                llmResult = await _llmProvider.GenerateAdrAsync(sanitizedDescription, sanitizedContext, ct);
            }
            catch (Exception ex) when (ex is not GovernanceException)
            {
                _logger.LogError(ex,
                    "[GovernanceService] Error al generar ADR con IA. ProjectId={ProjectId}",
                    request.ProjectId);
                throw new LlmUnavailableException(ex.Message);
            }

            // Post-response DLP: validar salida del LLM (ADR-P005)
            var dlpResult = await _dlpFilter.ValidateOutputAsync(llmResult.Content, ct);
            var sanitizedContent = dlpResult.SanitizedText;

            if (dlpResult.Findings.Count > 0)
            {
                _logger.LogWarning(
                    "[GovernanceService] DLP detectó PII en respuesta del LLM. Findings={Count} ProjectId={ProjectId}",
                    dlpResult.Findings.Count, request.ProjectId);
            }

            // Calcular siguiente número de ADR
            var existingAdrs = await _adrRepo.ListByProjectAsync(request.ProjectId, ct);
            var nextNumber = existingAdrs.Count > 0
                ? existingAdrs.Max(a => a.Number) + 1
                : 1;

            var adr = new Adr
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                Number = nextNumber,
                Title = llmResult.Title,
                Status = AdrStatus.PROPOSED,
                Content = sanitizedContent,
                CreatedBy = actorId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _adrRepo.CreateAsync(adr, ct);

            await _auditRepo.CreateAsync(new AuditLog
            {
                ProjectId = project.Id,
                EntityType = nameof(Adr),
                EntityId = adr.Id,
                Action = AuditAction.ADR_GENERATED_AI.ToString(),
                ActorId = actorId,
                CorrelationId = correlationId,
                Details = new Dictionary<string, object>
                {
                    ["number"] = adr.Number,
                    ["title"] = adr.Title,
                    ["model"] = llmResult.Model,
                    ["tokensUsed"] = llmResult.TokensUsed
                }
            }, ct);

            _logger.LogInformation(
                "[GovernanceService] ADR generado con IA. AdrId={AdrId} Number={Number} Model={Model} Tokens={Tokens}",
                adr.Id, adr.Number, llmResult.Model, llmResult.TokensUsed);

            return new CreateAdrWithAiResult
            {
                AdrId = adr.Id,
                ProjectId = project.Id,
                Title = adr.Title,
                Content = sanitizedContent,
                Model = llmResult.Model,
                TokensUsed = llmResult.TokensUsed,
                CreatedAt = adr.CreatedAt
            };
        }

        // ── ConfigureQualityGates ─────────────────────────────────────────────

        public async Task<ConfigureGatesResult> ConfigureQualityGatesAsync(
            ConfigureQualityGatesRequest request,
            Guid actorId,
            UserRole actorRole,
            Guid correlationId,
            CancellationToken ct = default)
        {
            EnsureWritePermission(actorRole);

            var project = await _projectRepo.GetByIdAsync(request.ProjectId, ct)
                ?? throw new ProjectNotFoundException(request.ProjectId);

            var gates = request.Gates.Select(g => new QualityGateCheck
            {
                Id = Guid.NewGuid(),
                ProjectId = project.Id,
                GateNumber = g.GateNumber,
                Name = g.Name,
                Validations = g.Validations,
                Blocking = g.Blocking,
                CreatedAt = DateTime.UtcNow
            }).ToList();

            await _gateRepo.UpsertProjectGatesAsync(project.Id, gates, ct);

            await _auditRepo.CreateAsync(new AuditLog
            {
                ProjectId = project.Id,
                EntityType = nameof(QualityGateCheck),
                EntityId = project.Id,
                Action = AuditAction.QUALITY_GATES_CONFIGURED.ToString(),
                ActorId = actorId,
                CorrelationId = correlationId,
                Details = new Dictionary<string, object>
                {
                    ["gateCount"] = gates.Count
                }
            }, ct);

            _logger.LogInformation(
                "[GovernanceService] Quality gates configurados. ProjectId={ProjectId} Count={Count}",
                project.Id, gates.Count);

            return new ConfigureGatesResult
            {
                ProjectId = project.Id,
                Count = gates.Count,
                UpdatedAt = DateTime.UtcNow
            };
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        private static void EnsureWritePermission(UserRole role)
        {
            if (!WriterRoles.Contains(role))
                throw new InsufficientRoleException("ARCHITECT o LEAD");
        }
    }
}
