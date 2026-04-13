namespace Application.Governance.Exceptions
{
    /// <summary>
    /// Excepción base para errores de dominio del módulo de Governance.
    /// Incluye HttpStatus y ErrorCode para mapeo directo en el controller.
    /// </summary>
    public class GovernanceException : Exception
    {
        public int HttpStatus { get; }
        public string ErrorCode { get; }

        public GovernanceException(int httpStatus, string errorCode, string message)
            : base(message)
        {
            HttpStatus = httpStatus;
            ErrorCode = errorCode;
        }
    }

    public class ProjectNotFoundException : GovernanceException
    {
        public ProjectNotFoundException(Guid projectId)
            : base(404, "PROJECT_NOT_FOUND", $"Proyecto con Id '{projectId}' no encontrado.") { }
    }

    public class ProjectAlreadyExistsException : GovernanceException
    {
        public ProjectAlreadyExistsException(string name)
            : base(409, "PROJECT_ALREADY_EXISTS", $"Ya existe un proyecto con nombre '{name}'.") { }
    }

    public class AdrNumberConflictException : GovernanceException
    {
        public AdrNumberConflictException(Guid projectId, int number)
            : base(409, "ADR_NUMBER_CONFLICT", $"Ya existe un ADR #{number} en el proyecto '{projectId}'.") { }
    }

    public class InsufficientRoleException : GovernanceException
    {
        public InsufficientRoleException(string requiredRole)
            : base(403, "FORBIDDEN", $"Se requiere rol '{requiredRole}' o superior para esta operación.") { }
    }

    public class LlmUnavailableException : GovernanceException
    {
        public LlmUnavailableException(string detail)
            : base(503, "LLM_UNAVAILABLE", $"El servicio de IA no está disponible: {detail}") { }
    }
}
