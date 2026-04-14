using Domain.Entities;
using Domain.Events;
using Domain.Interfaces;
using Infrastructure.Messaging;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Infrastructure.Workers
{
    /// <summary>
    /// Background worker que procesa jobs de generacion de specs desde la cola (ADR-P002).
    /// Implementa circuit breaker (3 fallos → pausa 60s) y reintentos con backoff exponencial.
    /// </summary>
    public class SpecGenerationWorker : BackgroundService
    {
        private readonly InMemorySpecGenerationQueue _queue;
        private readonly ISpecGenerationJobRepository _jobRepo;
        private readonly ISpecRepository _specRepo;
        private readonly IAdrRepository _adrRepo;
        private readonly ILlmProvider _llmProvider;
        private readonly IDlpFilter _dlpFilter;
        private readonly ITemplateProvider _templateProvider;
        private readonly IEventPublisher _eventPublisher;
        private readonly IAuditLogRepository _auditRepo;
        private readonly ILogger<SpecGenerationWorker> _logger;

        // Circuit breaker state
        private int _consecutiveFailures;
        private DateTime? _circuitOpenUntil;
        private const int CircuitBreakerThreshold = 3;
        private static readonly TimeSpan CircuitBreakerPause = TimeSpan.FromSeconds(60);

        // Retry config
        private const int MaxRetries = 3;
        private static readonly int[] RetryBackoffMs = { 1000, 2000, 4000 };

        public SpecGenerationWorker(
            InMemorySpecGenerationQueue queue,
            ISpecGenerationJobRepository jobRepo,
            ISpecRepository specRepo,
            IAdrRepository adrRepo,
            ILlmProvider llmProvider,
            IDlpFilter dlpFilter,
            ITemplateProvider templateProvider,
            IEventPublisher eventPublisher,
            IAuditLogRepository auditRepo,
            ILogger<SpecGenerationWorker> logger)
        {
            _queue = queue;
            _jobRepo = jobRepo;
            _specRepo = specRepo;
            _adrRepo = adrRepo;
            _llmProvider = llmProvider;
            _dlpFilter = dlpFilter;
            _templateProvider = templateProvider;
            _eventPublisher = eventPublisher;
            _auditRepo = auditRepo;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[SpecGenerationWorker] Worker iniciado.");

            await foreach (var job in _queue.Reader.ReadAllAsync(stoppingToken))
            {
                try
                {
                    await ProcessJobAsync(job, stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "[SpecGenerationWorker] Error no manejado procesando job. JobId={JobId}",
                        job.Id);
                }
            }
        }

        private async Task ProcessJobAsync(SpecGenerationJob job, CancellationToken ct)
        {
            // Circuit breaker check
            if (_circuitOpenUntil.HasValue && DateTime.UtcNow < _circuitOpenUntil.Value)
            {
                _logger.LogWarning(
                    "[SpecGenerationWorker] Circuit breaker abierto. JobId={JobId} ReabreEn={ReabreEn}",
                    job.Id, _circuitOpenUntil.Value);

                job.Status = SpecJobStatus.FAILED;
                job.Error = "LLM_UNAVAILABLE: circuit breaker abierto.";
                job.CompletedAt = DateTime.UtcNow;
                await _jobRepo.UpdateAsync(job, ct);
                return;
            }

            // Mark as processing
            job.Status = SpecJobStatus.PROCESSING;
            await _jobRepo.UpdateAsync(job, ct);

            _logger.LogInformation(
                "[SpecGenerationWorker] Procesando job. JobId={JobId} Level={Level}",
                job.Id, job.Level);

            // Build prompts
            var template = _templateProvider.GetSpecTemplate(job.Level);
            var userPrompt = BuildUserPrompt(job);

            // Enrich with ADRs context
            var adrs = await _adrRepo.ListByProjectAsync(job.ProjectId, ct);
            var adrContext = adrs.Count > 0
                ? "\n\n## ADRs vigentes del proyecto\n" +
                  string.Join("\n", adrs
                      .Where(a => a.Status == AdrStatus.ACCEPTED)
                      .Select(a => $"- ADR-{a.Number:D3}: {a.Title}"))
                : "";

            var systemPrompt = template + adrContext;

            // Enrich with parent spec context for L2/L3
            if (job.ParentSpecId.HasValue)
            {
                var parent = await _specRepo.GetByIdAsync(job.ParentSpecId.Value, ct);
                if (parent != null)
                {
                    var parentJson = JsonSerializer.Serialize(parent.Content);
                    userPrompt += $"\n\n## Spec parent ({parent.Level} - {parent.Title})\n{parentJson}";
                }
            }

            // Retry with exponential backoff
            LlmSpecResult? llmResult = null;
            for (int attempt = 0; attempt <= MaxRetries; attempt++)
            {
                try
                {
                    llmResult = await _llmProvider.GenerateSpecAsync(
                        job.Level, systemPrompt, userPrompt, ct);

                    // Success — reset circuit breaker
                    _consecutiveFailures = 0;
                    _circuitOpenUntil = null;
                    break;
                }
                catch (Exception ex) when (attempt < MaxRetries)
                {
                    job.RetryCount = attempt + 1;
                    await _jobRepo.UpdateAsync(job, ct);

                    _logger.LogWarning(ex,
                        "[SpecGenerationWorker] LLM falló. Reintento {Attempt}/{Max}. JobId={JobId}",
                        attempt + 1, MaxRetries, job.Id);

                    await Task.Delay(RetryBackoffMs[attempt], ct);
                }
                catch (Exception ex)
                {
                    // Max retries exhausted
                    _consecutiveFailures++;
                    if (_consecutiveFailures >= CircuitBreakerThreshold)
                    {
                        _circuitOpenUntil = DateTime.UtcNow.Add(CircuitBreakerPause);
                        _logger.LogError(
                            "[SpecGenerationWorker] Circuit breaker abierto. {Failures} fallos consecutivos. Pausa hasta {Until}",
                            _consecutiveFailures, _circuitOpenUntil);
                    }

                    job.Status = SpecJobStatus.FAILED;
                    job.Error = $"LLM_UNAVAILABLE: {ex.Message}";
                    job.CompletedAt = DateTime.UtcNow;
                    await _jobRepo.UpdateAsync(job, ct);

                    _logger.LogError(ex,
                        "[SpecGenerationWorker] Job fallido tras {MaxRetries} reintentos. JobId={JobId}",
                        MaxRetries, job.Id);
                    return;
                }
            }

            if (llmResult == null)
            {
                job.Status = SpecJobStatus.FAILED;
                job.Error = "LLM no retornó resultado.";
                job.CompletedAt = DateTime.UtcNow;
                await _jobRepo.UpdateAsync(job, ct);
                return;
            }

            // DLP post-response (ADR-006)
            var contentJson = JsonSerializer.Serialize(llmResult.Content);
            var dlpResult = await _dlpFilter.ValidateOutputAsync(contentJson, ct);

            if (dlpResult.Findings.Count > 0)
            {
                _logger.LogWarning(
                    "[SpecGenerationWorker] DLP detectó PII en respuesta. Findings={Count} JobId={JobId}",
                    dlpResult.Findings.Count, job.Id);

                // Reemplazar con contenido sanitizado
                var sanitizedContent = JsonSerializer.Deserialize<Dictionary<string, object>>(
                    dlpResult.SanitizedText) ?? llmResult.Content;
                llmResult = llmResult with { Content = sanitizedContent };
            }

            // Persist spec
            var spec = new Specification
            {
                Id = Guid.NewGuid(),
                ProjectId = job.ProjectId,
                ParentSpecId = job.ParentSpecId,
                Level = job.Level,
                Title = !string.IsNullOrEmpty(llmResult.Title) ? llmResult.Title : $"Spec {job.Level} - {DateTime.UtcNow:yyyy-MM-dd}",
                Version = "1.0.0",
                Status = SpecStatus.DRAFT,
                Content = llmResult.Content,
                Model = llmResult.Model,
                TokensUsed = llmResult.TokensUsed,
                CreatedBy = job.CreatedBy,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            await _specRepo.CreateAsync(spec, ct);

            // Update job
            job.Status = SpecJobStatus.COMPLETED;
            job.ResultSpecId = spec.Id;
            job.CompletedAt = DateTime.UtcNow;
            await _jobRepo.UpdateAsync(job, ct);

            // Publish event
            await _eventPublisher.PublishAsync(new SpecCreatedEvent
            {
                SpecId = spec.Id,
                ProjectId = spec.ProjectId,
                Level = spec.Level,
                Title = spec.Title,
                Version = spec.Version,
                CreatedBy = spec.CreatedBy,
                CreatedAt = spec.CreatedAt
            }, ct);

            // Audit log (no incluir input completo, solo sanitizado — ADR-006)
            await _auditRepo.CreateAsync(new AuditLog
            {
                ProjectId = spec.ProjectId,
                EntityType = nameof(Specification),
                EntityId = spec.Id,
                Action = AuditAction.SPEC_CREATED_AI.ToString(),
                ActorId = job.CreatedBy,
                CorrelationId = job.CorrelationId,
                Details = new Dictionary<string, object>
                {
                    ["level"] = spec.Level.ToString(),
                    ["model"] = llmResult.Model,
                    ["tokensUsed"] = llmResult.TokensUsed,
                    ["jobId"] = job.Id
                }
            }, ct);

            _logger.LogInformation(
                "[SpecGenerationWorker] Spec generada. SpecId={SpecId} JobId={JobId} Level={Level} Model={Model} Tokens={Tokens}",
                spec.Id, job.Id, spec.Level, llmResult.Model, llmResult.TokensUsed);

            if (llmResult.Warning != null)
            {
                _logger.LogWarning(
                    "[SpecGenerationWorker] Advertencia: {Warning} SpecId={SpecId}",
                    llmResult.Warning, spec.Id);
            }
        }

        private static string BuildUserPrompt(SpecGenerationJob job)
        {
            var sb = new System.Text.StringBuilder();

            sb.AppendLine(job.Level switch
            {
                SpecLevel.L1 => $"Genera una especificación de dominio (L1) para:",
                SpecLevel.L2 => $"Genera una especificación de sistema (L2) para:",
                SpecLevel.L3 => $"Genera una especificación de cambio (L3) para:",
                _ => "Genera una especificación para:"
            });

            sb.AppendLine();
            sb.AppendLine(job.SanitizedInput);

            if (!string.IsNullOrWhiteSpace(job.AdditionalContext))
            {
                sb.AppendLine();
                sb.AppendLine("## Contexto adicional");
                sb.AppendLine(job.AdditionalContext);
            }

            return sb.ToString();
        }
    }
}
