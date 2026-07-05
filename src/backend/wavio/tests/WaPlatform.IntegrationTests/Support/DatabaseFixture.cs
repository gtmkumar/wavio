using System.Diagnostics;
using System.Text;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace WaPlatform.IntegrationTests.Support;

/// <summary>
/// One throwaway <c>postgres:16</c> container per test run (issue #46), migrated through the real
/// <c>wavio.DbMigrator</c> — never <c>EnsureCreated</c>, never EF migrations (CLAUDE.md: versioned
/// SQL under db/migrations/V001..V013 is the only schema path). Shared by every test in the
/// "IntegrationTests" collection via <see cref="IntegrationTestCollection"/>.
///
/// ISOLATION STRATEGY (deliberately NOT ambient ttransactions, NOT TRUNCATE):
/// every test seeds its own fresh tenant (and whatever FK chain it needs) under a brand-new
/// <see cref="Guid.NewGuid"/>, rather than sharing fixture-wide rows or wrapping each test in a
/// rolled-back transaction. Two reasons, not just convenience:
///   1. The dispatcher race test (OutboxDispatcherFencedWriteTests) must open TWO independent
///      DbContext/connections that each see the OTHER's committed writes, to simulate two real
///      dispatcher instances racing one lease — an ambient per-test transaction would isolate
///      those connections from each other's writes (or from resolving statement-level or worse,
///      never durably commit at all) and hide exactly the race this test exists to catch.
///   2. Tests in this one xunit collection run SEQUENTIALLY by xunit v2's own default (test
///      collections run in parallel with each other; tests WITHIN one collection do not) — see
///      IntegrationTestCollection's doc comment — so there is no cross-test concurrency to guard
///      against, and no TRUNCATE-ordering-across-schemas coordination is needed either.
/// Rows are left behind after each test; the whole container is thrown away at the end of the run.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    private const string PinnedPostgresImage =
        // Same digest pinned in docker-compose.prod.yml (issue #42) — one deliberate re-pin
        // point for both prod and this test tier, not two independently-drifting version strings.
        "postgres:16@sha256:fe03a7605299a34ddf5e4f285dff78c3d7190a576b3c6b46f2fcff69f4bffd54";

    private PostgreSqlContainer? _container;

    /// <summary>Superuser connection string (Npgsql "Admin" convention, matching every
    /// appsettings.Development.json in this repo) — seeding fixtures and cross-tenant setup use
    /// this, since RLS's FORCE ROW LEVEL SECURITY would otherwise block inserting the very tenant
    /// row a test's own tenant-scoped connection needs to exist first.</summary>
    public string AdminConnectionString { get; private set; } = string.Empty;

    /// <summary>app_user connection string — what every test drives its actual handler/dispatcher
    /// code through, so RLS is genuinely enforced exactly as it is in production (WebApi
    /// ConnectionStrings:Default is app_user, never postgres/superuser).</summary>
    public string AppConnectionString { get; private set; } = string.Empty;

    public async Task InitializeAsync()
    {
        _container = new PostgreSqlBuilder(PinnedPostgresImage)
            .WithDatabase("waplatform")
            .WithUsername("postgres")
            .WithPassword("postgres")
            .Build();

        await _container.StartAsync();

        AdminConnectionString = _container.GetConnectionString();

        var adminBuilder = new NpgsqlConnectionStringBuilder(AdminConnectionString);
        AppConnectionString = new NpgsqlConnectionStringBuilder(AdminConnectionString)
        {
            Username = "app_user",
            Password = "app_user",
        }.ConnectionString;

        await ApplyMigrationsAsync(adminBuilder.ConnectionString);
    }

    public async Task DisposeAsync()
    {
        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>
    /// Applies db/migrations/V001..latest by INVOKING the real wavio.DbMigrator project (never
    /// reimplementing its apply-loop here) — the exact same "dotnet run --project
    /// src/backend/wavio/wavio.DbMigrator -- --connection-string ..." invocation
    /// .github/workflows/ci.yml's db-migrations job uses against its service-container Postgres,
    /// just pointed at this test's own throwaway container instead.
    /// </summary>
    private static async Task ApplyMigrationsAsync(string adminConnectionString)
    {
        var repoRoot = FindRepoRoot();
        var dbMigratorProject = Path.Combine(repoRoot, "src", "backend", "wavio", "wavio.DbMigrator", "wavio.DbMigrator.csproj");
        var migrationsDir = Path.Combine(repoRoot, "db", "migrations");

        if (!File.Exists(dbMigratorProject))
            throw new InvalidOperationException($"wavio.DbMigrator project not found at '{dbMigratorProject}'.");
        if (!Directory.Exists(migrationsDir))
            throw new InvalidOperationException($"db/migrations not found at '{migrationsDir}'.");

        var psi = new ProcessStartInfo("dotnet")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        psi.ArgumentList.Add("run");
        psi.ArgumentList.Add("--project");
        psi.ArgumentList.Add(dbMigratorProject);
        psi.ArgumentList.Add("--");
        psi.ArgumentList.Add("--connection-string");
        psi.ArgumentList.Add(adminConnectionString);
        psi.ArgumentList.Add("--migrations-dir");
        psi.ArgumentList.Add(migrationsDir);

        using var process = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) stdout.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) stderr.AppendLine(e.Data); };

        process.Start();
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        // Cold restore + build of wavio.DbMigrator plus applying 13 migration files comfortably
        // finishes well under this; generous ceiling so a slow CI runner's first-ever restore
        // doesn't flake.
        var completed = process.WaitForExit(TimeSpan.FromMinutes(5));

        if (!completed)
        {
            try { process.Kill(entireProcessTree: true); } catch { /* best-effort cleanup */ }
            throw new TimeoutException(
                "wavio.DbMigrator did not finish applying migrations within 5 minutes.\n" +
                $"--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        }

        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(
                $"wavio.DbMigrator exited with code {process.ExitCode}.\n" +
                $"--- stdout ---\n{stdout}\n--- stderr ---\n{stderr}");
        }
    }

    /// <summary>Walks up from this test assembly's build output directory looking for the repo
    /// root (identified by db/migrations existing under it) — same technique wavio.DbMigrator's
    /// own Program.cs uses to find its default migrations directory, applied here to find the
    /// repo root regardless of the test runner's working directory.</summary>
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 12 && dir is not null; i++, dir = dir.Parent)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "db", "migrations")))
                return dir.FullName;
        }

        throw new DirectoryNotFoundException(
            $"Could not locate repo root (a db/migrations directory) by walking up from '{AppContext.BaseDirectory}'.");
    }
}
