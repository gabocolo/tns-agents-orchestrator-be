namespace Domain.Entities
{
    public enum AuditAction
    {
        PROJECT_CREATED,
        ADR_CREATED,
        ADR_GENERATED_AI,
        QUALITY_GATES_CONFIGURED,
        SPEC_CREATED,
        SPEC_CREATED_AI,
        SPEC_SECTION_REGENERATED
    }
}
