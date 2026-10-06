using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Stockroom.Core.Identifiers;

namespace Stockroom.Core;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers the clock (<see cref="TimeProvider.System"/>) and <see cref="IIdGenerator"/>.
    /// Code that needs the current time takes a <see cref="TimeProvider"/> rather than reading
    /// <see cref="DateTimeOffset.UtcNow"/>, so tests can substitute a fake clock. An existing
    /// <see cref="TimeProvider"/> registration is kept.
    /// </summary>
    public static IServiceCollection AddStockroomCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IIdGenerator, IdGenerator>();
        return services;
    }
}
