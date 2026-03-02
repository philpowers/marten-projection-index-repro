using JasperFx.Events.Projections;
using Marten;
using Marten.Events.Projections;
using Npgsql;
using Shouldly;
using Testcontainers.PostgreSql;
using Xunit;

namespace MartenRepro.Tests;

/// <summary>
/// Demonstrates that ConfigureMarten (which adds indexes via DocumentMapping)
/// is discovered for Snapshot projections but NOT for MultiStreamProjection views.
/// </summary>
public class ProjectionIndexTests : IAsyncLifetime
{
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder()
        .WithImage("postgres:17")
        .Build();

    private IDocumentStore _store = null!;

    public async Task InitializeAsync()
    {
        await _postgres.StartAsync();

        _store = DocumentStore.For(opts =>
        {
            opts.Connection(_postgres.GetConnectionString());
            opts.Projections.Snapshot<TeamSummary>(SnapshotLifecycle.Inline);
            opts.Projections.Add<TeamMembersProjection>(ProjectionLifecycle.Inline);
        });

        // Append events to force schema + projection creation
        await using var session = _store.LightweightSession();

        var teamId = Guid.NewGuid();

        session.Events.StartStream(
            teamId,
            new TeamCreatedV1("Acme", DateTimeOffset.UtcNow));

        session.Events.StartStream(
            Guid.NewGuid(),
            new MemberJoinedTeamV1(teamId, "user-1", DateTimeOffset.UtcNow));

        await session.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        _store.Dispose();
        await _postgres.DisposeAsync();
    }

    [Fact]
    public async Task SnapshotView_ConfigureMarten_CreatesIndex()
    {
        // TeamSummary is registered via Projections.Snapshot<T>() which calls Schema.For<T>(),
        // triggering the DocumentMapping<T> constructor and discovering ConfigureMarten.
        var indexExists = await IndexExistsAsync("mt_doc_teamsummary", "name");
        indexExists.ShouldBeTrue(
            "Expected a computed index on TeamSummary.Name (configured via ConfigureMarten). " +
            "Snapshot<T>() calls Schema.For<T>(), so ConfigureMarten is discovered.");
    }

    [Fact]
    public async Task MultiStreamView_ConfigureMarten_CreatesIndex()
    {
        // TeamMembersView is registered via Projections.Add<TeamMembersProjection>() which
        // does NOT call Schema.For<TeamMembersView>(), so the DocumentMapping<T> constructor
        // never runs and ConfigureMarten is never discovered.
        var indexExists = await IndexExistsAsync("mt_doc_teammembersview", "team_id");
        indexExists.ShouldBeTrue(
            "Expected a computed index on TeamMembersView.TeamId (configured via ConfigureMarten). " +
            "Projections.Add<T>() does NOT call Schema.For<TDoc>(), so ConfigureMarten is never discovered. " +
            "This is the bug.");
    }

    private async Task<bool> IndexExistsAsync(string tableName, string indexSubstring)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM pg_indexes
            WHERE tablename = @table
              AND indexdef ILIKE @pattern
            """;
        cmd.Parameters.AddWithValue("table", tableName);
        cmd.Parameters.AddWithValue("pattern", $"%{indexSubstring}%");

        var count = (long)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }
}
