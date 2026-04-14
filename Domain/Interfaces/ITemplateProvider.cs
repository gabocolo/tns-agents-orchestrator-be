using Domain.Entities;

namespace Domain.Interfaces
{
    public interface ITemplateProvider
    {
        /// <summary>
        /// Obtiene el template de prompt para generacion de spec segun nivel (L1/L2/L3).
        /// </summary>
        string GetSpecTemplate(SpecLevel level);

        /// <summary>
        /// Obtiene las secciones validas para un nivel de spec (para validar RegenerateSection).
        /// </summary>
        List<string> GetValidSections(SpecLevel level);
    }
}
