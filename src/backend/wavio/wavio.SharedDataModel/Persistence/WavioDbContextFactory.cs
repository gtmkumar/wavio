using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace wavio.SharedDataModel.Persistence;

/// <summary>
/// Design-time factory used by EF Core tooling (dotnet ef dbcontext info, scaffold, etc.).
/// Connects to the local development database.
/// NOTE: Never run "dotnet ef migrations add" or "database update" from this project —
/// the live DB schema is canonical and migrations are not generated from this library.
/// </summary>
public sealed class WavioDbContextFactory : IDesignTimeDbContextFactory<WavioDbContext>
{
    public WavioDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<WavioDbContext>();

        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=wavio_db;Username=postgres;Password=postgres",
            npgsql => npgsql.UseNetTopologySuite());

        return new WavioDbContext(optionsBuilder.Options);
    }
}
