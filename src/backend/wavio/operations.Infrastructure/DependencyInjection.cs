using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using operations.Application.Common.Interfaces;
using operations.Infrastructure.Persistence;
using operations.Infrastructure.Storage;

namespace operations.Infrastructure;

/// <summary>
/// DI registration for the operations Infrastructure layer. Registers the operations data-access
/// surface (<see cref="IOperationsDbContext"/>) over the shared context plus the file-storage
/// provider (migrated from the legacy ServiceDefaults.Storage). Handlers depend on interfaces;
/// no repositories. Mirrors core.Infrastructure.
/// Call from the host: <c>builder.Services.AddOperationsInfrastructure(builder.Configuration);</c>
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddOperationsInfrastructure(
        this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped<IOperationsDbContext, OperationsDbContext>();

        AddFileStorage(services, configuration);

        return services;
    }

    /// <summary>
    /// Registers the <see cref="IFileStorageProvider"/> implementation selected by
    /// <c>Storage:Provider</c> (default <c>local</c>). Used by the inspection-photo upload/stream slice.
    /// </summary>
    private static void AddFileStorage(IServiceCollection services, IConfiguration configuration)
    {
        var providerName = FileStorageProviderFactory.ResolveProviderName(configuration);

        if (providerName == "local")
        {
            // Bind via an explicit lambda (not Configure<T>(IConfiguration)) to stay AOT/trim-safe —
            // the reflection-based overload trips IL2026/IL3050 in this IsAotCompatible project.
            var rootPath = configuration["Storage:Local:RootPath"];
            services.Configure<LocalStorageOptions>(opts =>
            {
                if (!string.IsNullOrWhiteSpace(rootPath))
                    opts.RootPath = rootPath;
            });
            services.AddSingleton<IFileStorageProvider, LocalFileStorageProvider>();
        }

        // Cloud provider registrations go here once wired (see FileStorageProviderFactory seams).
    }
}
