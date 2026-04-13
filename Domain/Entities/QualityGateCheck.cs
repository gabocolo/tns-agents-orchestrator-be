namespace Domain.Entities
{
    public class QualityGateCheck
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public Guid ProjectId { get; set; }
        public int GateNumber { get; set; }
        public string Name { get; set; } = string.Empty;
        public List<string> Validations { get; set; } = new();
        public bool Blocking { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
