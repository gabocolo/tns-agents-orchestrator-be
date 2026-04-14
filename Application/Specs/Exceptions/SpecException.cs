namespace Application.Specs.Exceptions
{
    public class SpecException : Exception
    {
        public int HttpStatus { get; }
        public string ErrorCode { get; }

        public SpecException(int httpStatus, string errorCode, string message)
            : base(message)
        {
            HttpStatus = httpStatus;
            ErrorCode = errorCode;
        }
    }

    public class ParentNotApprovedException : SpecException
    {
        public ParentNotApprovedException(Guid parentId)
            : base(422, "PARENT_NOT_APPROVED",
                  $"El spec parent '{parentId}' no está en estado APPROVED.") { }
    }

    public class ParentNotFoundException : SpecException
    {
        public ParentNotFoundException(Guid parentId)
            : base(404, "PARENT_NOT_FOUND",
                  $"El spec parent '{parentId}' no existe.") { }
    }

    public class ParentWrongLevelException : SpecException
    {
        public ParentWrongLevelException(string expectedLevel, string actualLevel)
            : base(422, "PARENT_WRONG_LEVEL",
                  $"Se requiere parent de nivel {expectedLevel}, pero se recibió {actualLevel}.") { }
    }

    public class DuplicateSpecException : SpecException
    {
        public DuplicateSpecException(string title, string level)
            : base(409, "DUPLICATE_SPEC",
                  $"Ya existe una spec '{title}' de nivel {level} en estado DRAFT o IN_REVIEW.") { }
    }

    public class IncompleteStructureException : SpecException
    {
        public IncompleteStructureException(string missingFields)
            : base(422, "INCOMPLETE_STRUCTURE",
                  $"Faltan campos obligatorios: {missingFields}.") { }
    }

    public class MissingDataClassificationException : SpecException
    {
        public MissingDataClassificationException()
            : base(422, "MISSING_DATA_CLASSIFICATION",
                  "Las specs de nivel L1 requieren clasificación de datos.") { }
    }

    public class InvalidVersionException : SpecException
    {
        public InvalidVersionException(string version)
            : base(422, "INVALID_VERSION",
                  $"La versión '{version}' no cumple formato semver (X.Y.Z).") { }
    }

    public class SpecLlmUnavailableException : SpecException
    {
        public SpecLlmUnavailableException(string detail)
            : base(503, "LLM_UNAVAILABLE",
                  $"El servicio de IA no está disponible: {detail}. Puede crear la spec manualmente.") { }
    }

    public class SpecLlmTimeoutException : SpecException
    {
        public SpecLlmTimeoutException(int timeoutSeconds)
            : base(504, "LLM_TIMEOUT",
                  $"La generación excedió el timeout de {timeoutSeconds}s.") { }
    }

    public class DlpViolationException : SpecException
    {
        public DlpViolationException(int findingsCount)
            : base(500, "DLP_VIOLATION",
                  $"DLP post-response detectó {findingsCount} hallazgo(s) de PII en la respuesta del LLM.") { }
    }

    public class SpecUnauthorizedException : SpecException
    {
        public SpecUnauthorizedException()
            : base(403, "UNAUTHORIZED",
                  "No tiene permisos para crear specs. Se requiere rol ARCHITECT, LEAD o SENIOR_DEV.") { }
    }

    public class SectionNotFoundException : SpecException
    {
        public SectionNotFoundException(string section)
            : base(400, "SECTION_NOT_FOUND",
                  $"La sección '{section}' no existe en la spec.") { }
    }

    public class SpecNotFoundException : SpecException
    {
        public SpecNotFoundException(Guid specId)
            : base(404, "SPEC_NOT_FOUND",
                  $"La spec '{specId}' no existe.") { }
    }

    public class ProjectNotFoundForSpecException : SpecException
    {
        public ProjectNotFoundForSpecException(Guid projectId)
            : base(404, "PROJECT_NOT_FOUND",
                  $"El proyecto '{projectId}' no existe.") { }
    }
}
