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
    private readonly PostgreSqlContainer _postgres = new PostgreSqlBuilder("postgres:17")
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
        // CONTROL: Snapshot<T>() calls Schema.For<T>(), so ConfigureMarten is discovered.
        // This test should PASS, proving the convention works when the code path is hit.

        TeamSummary.ConfigureMartenWasCalled.ShouldBeTrue(
            "ConfigureMarten was never called on TeamSummary. " +
            "Snapshot<T>() should call Schema.For<T>(), triggering the convention.");

        // The index configured in ConfigureMarten should exist in the database.
        var indexes = await GetIndexesAsync("mt_doc_teamsummary");
        indexes.ShouldContain(
            idx => idx.Contains("name", StringComparison.OrdinalIgnoreCase),
            $"Expected a computed index on TeamSummary.Name. Actual indexes:\n{FormatIndexList(indexes)}");
    }

    [Fact]
    public void MultiStreamView_ConfigureMarten_IsDiscovered()
    {
        // BUG: Projections.Add<T>() does not call Schema.For<TDoc>(), so the
        // DocumentMapping<T> constructor never runs and ConfigureMarten is never discovered.
        // This test FAILS, demonstrating the bug.

        TeamMembersView.ConfigureMartenWasCalled.ShouldBeTrue(
            "ConfigureMarten was never called on TeamMembersView. " +
            "Projections.Add<T>() does not call Schema.For<TDoc>(), " +
            "so the ConfigureMarten convention is never discovered. This is the bug.");
    }

    [Fact]
    public async Task MultiStreamView_ConfigureMarten_CreatesIndex()
    {
        // BUG (consequence): Because ConfigureMarten is never called, the index it
        // configures is never created. This test FAILS, demonstrating the consequence.

        // Sanity check: the projection ran and the table + document exist.
        var tableExists = await TableExistsAsync("mt_doc_teammembersview");
        tableExists.ShouldBeTrue(
            "Precondition failed: mt_doc_teammembersview table does not exist. " +
            "The MultiStreamProjection did not run - check event setup.");

        var indexes = await GetIndexesAsync("mt_doc_teammembersview");
        indexes.ShouldContain(
            idx => idx.Contains("team_id", StringComparison.OrdinalIgnoreCase),
            $"Expected a computed index on TeamMembersView.TeamId. Actual indexes:\n{FormatIndexList(indexes)}");
    }

    private async Task<bool> TableExistsAsync(string tableName)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT COUNT(*) FROM information_schema.tables
            WHERE table_name = @table
            """;
        cmd.Parameters.AddWithValue("table", tableName);

        var count = (long)(await cmd.ExecuteScalarAsync())!;
        return count > 0;
    }

    private async Task<List<string>> GetIndexesAsync(string tableName)
    {
        await using var conn = new NpgsqlConnection(_postgres.GetConnectionString());
        await conn.OpenAsync();

        await using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            SELECT indexname, indexdef FROM pg_indexes
            WHERE tablename = @table
            ORDER BY indexname
            """;
        cmd.Parameters.AddWithValue("table", tableName);

        var indexes = new List<string>();
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            indexes.Add($"{reader.GetString(0)}: {reader.GetString(1)}");
        }

        return indexes;
    }

    private static string FormatIndexList(List<string> indexes) =>
        indexes.Count == 0
            ? "  (none)"
            : string.Join("\n", indexes.Select(idx => $"  - {idx}"));
}
