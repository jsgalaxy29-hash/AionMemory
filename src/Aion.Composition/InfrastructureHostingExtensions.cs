using System;
using System.Threading;
using System.Threading.Tasks;
using Aion.AI;
using Aion.Domain;
using Aion.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aion.Composition;

public static class InfrastructureHostingExtensions
{
    public static IServiceCollection AddAionInfrastructure(this IServiceCollection services, IConfiguration configuration)
        => Aion.Infrastructure.DependencyInjectionExtensions.AddAionInfrastructure(services, configuration);


    public static IServiceCollection AddAionCore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddAionInfrastructure(configuration);
        services.AddAionAi(configuration);
        services.ApplyAionPlatformDefaults();
        return services;
    }

    public static IServiceCollection ApplyAionPlatformDefaults(this IServiceCollection services)
    {
        var enableBackground = !(OperatingSystem.IsAndroid() || OperatingSystem.IsIOS());
        services.Configure<BackupOptions>(options => options.EnableBackgroundServices = enableBackground);
        services.Configure<AutomationSchedulerOptions>(options => options.EnableBackgroundServices = enableBackground);
        return services;
    }

    public static Task EnsureAionDatabaseAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        => Aion.Infrastructure.DependencyInjectionExtensions.EnsureAionDatabaseAsync(serviceProvider, cancellationToken);
}
