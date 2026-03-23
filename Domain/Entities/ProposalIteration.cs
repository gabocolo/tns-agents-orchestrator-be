namespace Domain.Entities
{
    public class ProposalIteration
    {
        public int Version { get; set; }

        /// <summary>
        /// Markdown completo de la arquitectura generado por el ArchitectureAgent.
        /// Angular lo renderiza directamente con ngx-markdown.
        /// </summary>
        public string Content { get; set; } = string.Empty;

        public List<string> Components { get; set; } = new();

        public int TeamSize { get; set; }
        public int DurationWeeks { get; set; }
        public decimal BudgetUsd { get; set; }
        public RiskLevel RiskLevel { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
