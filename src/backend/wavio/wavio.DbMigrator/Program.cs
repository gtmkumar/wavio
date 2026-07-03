// wavio.DbMigrator — applies db/migrations/V00N__*.sql forward-only, in filename order,
// tracked in public.schema_migrations (see db/README.md "Migration convention").
//
// Usage:
//   dotnet run --project src/backend/wavio/wavio.DbMigrator
//   dotnet run --project src/backend/wavio/wavio.DbMigrator -- --connection-string "Host=...;..."
//   dotnet run --project src/backend/wavio/wavio.DbMigrator -- --migrations-dir /path/to/db/migrations
//
// Connects with the ADMIN (superuser) connection — migrations create schemas/roles/policies
// that app_user is not permitted to create. Never point this at app_user.
//
// Each V00N file is applied as a single transaction; the file's own trailing
// `INSERT INTO public.schema_migrations ... ON CONFLICT DO NOTHING` is the completion
// marker the runner relies on for idempotency (re-running is always safe).

using Npgsql;

var connectionString = ResolveConnectionString(args);
var migrationsDir = ResolveMigrationsDirectory(args);

Console.WriteLine($"[wavio.DbMigrator] Migrations directory: {migrationsDir}");
Console.WriteLine($"[wavio.DbMigrator] Target: {DescribeConnection(connectionString)}");

var files = Directory.GetFiles(migrationsDir, "V*.sql")
    .OrderBy(f => Path.GetFileName(f), StringComparer.Ordinal)
    .ToArray();

if (files.Length == 0)
{
    Console.Error.WriteLine($"[wavio.DbMigrator] No V*.sql files found in {migrationsDir}.");
    return 1;
}

await using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

var applied = await GetAppliedVersionsAsync(connection);
var appliedCount = 0;

foreach (var file in files)
{
    var version = ExtractVersion(file);

    if (applied.Contains(version))
    {
        Console.WriteLine($"[wavio.DbMigrator] {version} — already applied, skipping.");
        continue;
    }

    Console.WriteLine($"[wavio.DbMigrator] Applying {Path.GetFileName(file)} ...");
    var sql = await File.ReadAllTextAsync(file);

    await using var tx = await connection.BeginTransactionAsync();
    try
    {
        await using var cmd = new NpgsqlCommand(sql, connection, tx) { CommandTimeout = 120 };
        await cmd.ExecuteNonQueryAsync();
        await tx.CommitAsync();
    }
    catch (Exception ex)
    {
        await tx.RollbackAsync();
        Console.Error.WriteLine($"[wavio.DbMigrator] {version} FAILED — rolled back. {ex.Message}");
        return 1;
    }

    appliedCount++;
    Console.WriteLine($"[wavio.DbMigrator] {version} applied.");
}

Console.WriteLine(appliedCount == 0
    ? "[wavio.DbMigrator] Database already up to date — nothing to apply."
    : $"[wavio.DbMigrator] Applied {appliedCount} migration(s).");

return 0;

// ─────────────────────────────────────────────────────────────────────────────

static string ExtractVersion(string filePath)
{
    var name = Path.GetFileNameWithoutExtension(filePath); // "V001__tenancy"
    var separatorIndex = name.IndexOf("__", StringComparison.Ordinal);
    return separatorIndex < 0 ? name : name[..separatorIndex]; // "V001"
}

static async Task<HashSet<string>> GetAppliedVersionsAsync(NpgsqlConnection connection)
{
    // schema_migrations itself is created by V001 — on a fresh database it doesn't exist
    // yet, which just means "nothing applied", not an error. Cast regclass::text — Npgsql
    // has no generic object mapping for the regclass type itself.
    await using var checkCmd = new NpgsqlCommand("SELECT to_regclass('public.schema_migrations')::text", connection);
    var tableExists = await checkCmd.ExecuteScalarAsync() is not (null or DBNull);
    if (!tableExists) return [];

    var applied = new HashSet<string>(StringComparer.Ordinal);
    await using var cmd = new NpgsqlCommand("SELECT version FROM public.schema_migrations", connection);
    await using var reader = await cmd.ExecuteReaderAsync();
    while (await reader.ReadAsync())
        applied.Add(reader.GetString(0));

    return applied;
}

static string ResolveConnectionString(string[] args)
{
    var fromArgs = GetArgValue(args, "--connection-string");
    if (!string.IsNullOrWhiteSpace(fromArgs)) return fromArgs;

    var fromEnv = Environment.GetEnvironmentVariable("ConnectionStrings__Admin");
    if (!string.IsNullOrWhiteSpace(fromEnv)) return fromEnv;

    // Matches docker-compose.dev.yml / wavio.AppHost's Admin fallback (issue #9).
    return "Host=localhost;Port=5432;Database=waplatform;Username=postgres;Password=postgres";
}

static string ResolveMigrationsDirectory(string[] args)
{
    var fromArgs = GetArgValue(args, "--migrations-dir");
    if (!string.IsNullOrWhiteSpace(fromArgs)) return Path.GetFullPath(fromArgs);

    // Walk up from the build output directory looking for repo-root/db/migrations —
    // stable regardless of the caller's current working directory.
    var dir = new DirectoryInfo(AppContext.BaseDirectory);
    for (var i = 0; i < 10 && dir is not null; i++, dir = dir.Parent)
    {
        var candidate = Path.Combine(dir.FullName, "db", "migrations");
        if (Directory.Exists(candidate)) return candidate;
    }

    throw new DirectoryNotFoundException(
        "Could not locate db/migrations by walking up from the build output directory. " +
        "Pass --migrations-dir explicitly.");
}

static string? GetArgValue(string[] args, string name)
{
    for (var i = 0; i < args.Length; i++)
    {
        if (args[i] == name && i + 1 < args.Length) return args[i + 1];
        if (args[i].StartsWith(name + "=", StringComparison.Ordinal)) return args[i][(name.Length + 1)..];
    }
    return null;
}

static string DescribeConnection(string connectionString)
{
    // Never print the password.
    var builder = new NpgsqlConnectionStringBuilder(connectionString);
    return $"{builder.Host}:{builder.Port}/{builder.Database} as {builder.Username}";
}
