using JasperFx.Events;
using Marten.Events.Projections;
using Marten.Schema;

namespace MartenRepro;

/// <summary>
/// View document for a MultiStreamProjection.
/// ConfigureMarten adds a computed index on TeamId.
/// This is the BUG case - ConfigureMarten is NOT discovered for multi-stream projections.
/// </summary>
public class TeamMembersView
{
    public string Id { get; set; } = "";
    public Guid TeamId { get; set; }
    public string UserId { get; set; } = "";
    public DateTimeOffset JoinedAtUtc { get; set; }

    public static void ConfigureMarten(DocumentMapping<TeamMembersView> mapping)
    {
        mapping.Index(x => x.TeamId);
    }
}

public class TeamMembersProjection : MultiStreamProjection<TeamMembersView, string>
{
    public TeamMembersProjection()
    {
        Identity<IEvent<MemberJoinedTeamV1>>(e => $"{e.Data.TeamId}/{e.Data.UserId}");
    }

    public TeamMembersView Create(MemberJoinedTeamV1 e) =>
        new()
        {
            TeamId = e.TeamId,
            UserId = e.UserId,
            JoinedAtUtc = e.JoinedAtUtc
        };
}
