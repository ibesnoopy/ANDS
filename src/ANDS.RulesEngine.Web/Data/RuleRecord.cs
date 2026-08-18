namespace ANDS.RulesEngine.Web.Data;

/// <summary>
/// Row shape of the rules table consumed by the engine's SQL rule source.
/// </summary>
public class RuleRecord
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }
    public int Priority { get; set; }
    public bool Enabled { get; set; } = true;
    public string Definition { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
    public string? UpdatedBy { get; set; }
}

public class RuleAudit
{
    public int Id { get; set; }
    public string RuleId { get; set; } = string.Empty;
    public string Action { get; set; } = string.Empty;
    public string? Definition { get; set; }
    public string ChangedBy { get; set; } = string.Empty;
    public DateTimeOffset ChangedAt { get; set; }
}
