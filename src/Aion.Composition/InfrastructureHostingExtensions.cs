using System;
using System.Threading;
using System.Threading.Tasks;
using Aion.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Aion.Composition;

public static class InfrastructureHostingExtensions
{
    public static IServiceCollection AddAionInfrastructure(this IServiceCollection services, IConfiguration configuration)
        => Aion.Infrastructure.DependencyInjectionExtensions.AddAionInfrastructure(services, configuration);

    public static Task EnsureAionDatabaseAsync(this IServiceProvider serviceProvider, CancellationToken cancellationToken = default)
        => Aion.Infrastructure.DependencyInjectionExtensions.EnsureAionDatabaseAsync(serviceProvider, cancellationToken);
}
