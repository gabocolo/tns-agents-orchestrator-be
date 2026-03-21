namespace Domain.Entities
{
    /// <summary>
    /// Filtros para listar propuestas. Todos los campos son opcionales.
    /// </summary>
    public class ProposalFilters
    {
        public ProposalStatus? Status { get; set; }

        /// <summary>
        /// Filtra propuestas donde el usuario participa con el rol indicado.
        /// Si Role es null, devuelve propuestas donde el usuario participa en cualquier rol.
        /// </summary>
        public string? UserId { get; set; }
        public ProposalRole? Role { get; set; }
    }
}
