using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Aion.AI;
using Aion.Domain;
using Aion.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Aion.Composition;

public static class InfrastructureHostingExtensions
{
    public static IServiceCollection AddAionInfrastructure(this IServiceCollection services, IConfiguration configuration)
        => Aion.Infrastructure.DependencyInjectionExtensions.AddAionInfrastructure(services, configuration);

    public static IServiceCollection AddAionCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAionInfrastructure(configuration);
        services.AddAionAi(configuration);
        services.ApplyAionPlatformDefaults(enableBackgroundServices: IsBackgroundServicesEnabledByPlatform());
        return services;
    }

    public static IServiceCollection ApplyAionPlatformDefaults(this IServiceCollection services)
        => services.ApplyAionPlatformDefaults(enableBackgroundServices: IsBackgroundServicesEnabledByPlatform());

    public static IServiceCollection ApplyAionPlatformDefaults(this IServiceCollection services, bool enableBackgroundServices)
    {
        var markerType = typeof(AionPlatformDefaultsMarker);
        if (services.Any(descriptor => descriptor.ServiceType == markerType))
        {
            return services;
        }

        services.TryAddSingleton<AionPlatformDefaultsMarker>();
        // Keep explicit, separate post-configurations for each options type.
        services.PostConfigure<BackupOptions>(options => options.EnableBackgroundServices = enableBackgroundServices);
        services.PostConfigure<AutomationSchedulerOptions>(options => options.EnableBackgroundServices = enableBackgroundServices);
        return services;
    }

    private static bool IsBackgroundServicesEnabledByPlatform() =>
        !(OperatingSystem.IsAndroid() || OperatingSystem.IsIOS());

    private sealed class AionPlatformDefaultsMarker;

    public static Task EnsureAionDatabaseAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        => Aion.Infrastructure.DependencyInjectionExtensions.EnsureAionDatabaseAsync(serviceProvider, cancellationToken);
}
