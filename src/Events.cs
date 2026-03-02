namespace MartenRepro;

public record TeamCreatedV1(string Name, DateTimeOffset CreatedAtUtc);

public record MemberJoinedTeamV1(Guid TeamId, string UserId, DateTimeOffset JoinedAtUtc);
