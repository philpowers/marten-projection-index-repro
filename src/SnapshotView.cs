using Marten.Schema;

namespace MartenRepro;

/// <summary>
/// Self-aggregating snapshot view registered via Projections.Snapshot&lt;T&gt;().
/// ConfigureMarten adds a computed index on Name.
/// This is the CONTROL case - ConfigureMarten IS discovered for snapshot projections.
/// </summary>
public class TeamSummary
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public DateTimeOffset CreatedAtUtc { get; set; }

    public static void ConfigureMarten(DocumentMapping<TeamSummary> mapping)
    {
        mapping.Index(x => x.Name);
    }

    public static TeamSummary Create(TeamCreatedV1 e) =>
        new() { Name = e.Name, CreatedAtUtc = e.CreatedAtUtc };
}
