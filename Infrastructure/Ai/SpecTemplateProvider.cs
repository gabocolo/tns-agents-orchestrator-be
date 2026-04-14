using Domain.Entities;
using Domain.Interfaces;

namespace Infrastructure.Ai
{
    public class SpecTemplateProvider : ITemplateProvider
    {
        private static readonly Dictionary<SpecLevel, List<string>> ValidSections = new()
        {
            [SpecLevel.L1] = new() { "objetivo", "entidades", "reglas", "casosDeUso", "validaciones" },
            [SpecLevel.L2] = new() { "descripcion", "contratoTecnico", "dependencias", "errores", "requisitos", "proteccionDatos", "testsGWT", "referencias" },
            [SpecLevel.L3] = new() { "cambio", "impactoContrato", "impactoComportamiento", "testsImpactados" }
        };

        public string GetSpecTemplate(SpecLevel level) => level switch
        {
            SpecLevel.L1 => L1Template,
            SpecLevel.L2 => L2Template,
            SpecLevel.L3 => L3Template,
            _ => throw new ArgumentOutOfRangeException(nameof(level))
        };

        public List<string> GetValidSections(SpecLevel level) =>
            ValidSections.TryGetValue(level, out var sections)
                ? sections
                : new List<string>();

        // ── Templates ────────────────────────────────────────────────────────

        private const string L1Template = """
            Eres un arquitecto de software senior. Genera una especificación de dominio (Nivel L1)
            en formato JSON con la siguiente estructura exacta:

            {
              "objetivo": "Descripción del dominio y problema que resuelve",
              "entidades": [
                {
                  "nombre": "NombreEntidad",
                  "descripcion": "Descripción",
                  "atributos": [{"nombre": "attr", "tipo": "string", "requerido": true}],
                  "relaciones": [{"entidad": "OtraEntidad", "tipo": "1:N"}]
                }
              ],
              "reglas": [
                {"id": "RN-01", "descripcion": "Regla de negocio"}
              ],
              "casosDeUso": [
                {"id": "CU-01", "nombre": "NombreCasoDeUso", "descripcion": "Descripción", "actor": "Usuario"}
              ],
              "validaciones": [
                {"campo": "campo", "regla": "obligatorio|formato|rango", "mensaje": "Error si falla"}
              ]
            }

            Responde SOLO con el JSON, sin explicaciones, sin markdown, sin bloques de código.
            No incluyas datos personales, credenciales ni información sensible.
            Usa datos sintéticos en todos los ejemplos.
            """;

        private const string L2Template = """
            Eres un arquitecto de software senior. Genera una especificación de sistema (Nivel L2)
            en formato JSON con la siguiente estructura exacta:

            {
              "descripcion": "Descripción técnica del caso de uso",
              "contratoTecnico": {
                "inputs": [{"campo": "nombre", "tipo": "string", "requerido": true, "descripcion": "Desc"}],
                "outputs": [{"campo": "nombre", "tipo": "string", "descripcion": "Desc"}]
              },
              "dependencias": [
                {"nombre": "NombreServicio", "tipo": "Puerto", "proposito": "Descripción"}
              ],
              "errores": [
                {"codigo": "ERROR_CODE", "descripcion": "Descripción", "httpStatus": 422}
              ],
              "requisitos": {
                "latencia": "< Xms (p95)",
                "asincronia": "Descripción si aplica",
                "circuitBreaker": "Regla si aplica",
                "reintentos": "Política si aplica",
                "idempotencia": "Mecanismo si aplica"
              },
              "proteccionDatos": [
                {"campo": "campo", "clasificacion": "Confidencial|Interno|Sensible", "tratamiento": "Acción"}
              ],
              "testsGWT": [
                {"id": "GWT-01", "nombre": "Nombre del test", "given": "Precondición", "when": "Acción", "then": "Resultado esperado"}
              ],
              "referencias": ["REF-001"]
            }

            Responde SOLO con el JSON, sin explicaciones, sin markdown, sin bloques de código.
            No incluyas datos personales, credenciales ni información sensible.
            """;

        private const string L3Template = """
            Eres un arquitecto de software senior. Genera una especificación de cambio (Nivel L3)
            en formato JSON con la siguiente estructura exacta:

            {
              "cambio": "Descripción del cambio solicitado",
              "impactoContrato": "Descripción de cómo cambia el contrato técnico (inputs/outputs)",
              "impactoComportamiento": "Descripción de cómo cambia el comportamiento del sistema",
              "testsImpactados": [
                {"id": "GWT-XX", "tipo": "nuevo|modificado|eliminado", "descripcion": "Descripción del impacto"}
              ]
            }

            Si estimas que el cambio generará más de 400 líneas de código, incluye un campo adicional:
            "advertencia": "El cambio estimado supera 400 líneas. Considere dividir en múltiples L3."

            Responde SOLO con el JSON, sin explicaciones, sin markdown, sin bloques de código.
            No incluyas datos personales, credenciales ni información sensible.
            """;
    }
}
