using Microsoft.Extensions.Options;

namespace Stockroom.Api.Configuration;

internal static class StockroomOptionsExtensions
{
    /// <summary>
    /// Registers <see cref="StockroomOptions"/>, validated when the host starts, and applies
    /// <c>STOCKROOM_LOG_LEVEL</c> as the default log level.
    /// </summary>
    public static IServiceCollection AddStockroomOptions(this IServiceCollection services)
    {
        services.AddSingleton<StockroomOptionsSetup>();
        services.AddSingleton<IConfigureOptions<StockroomOptions>>(sp => sp.GetRequiredService<StockroomOptionsSetup>());
        services.AddSingleton<IValidateOptions<StockroomOptions>>(sp => sp.GetRequiredService<StockroomOptionsSetup>());
        services.AddOptions<StockroomOptions>().ValidateOnStart();

        // Logging is built before the host starts, so read the level without validating: an invalid
        // value falls back to the default here and is reported by ValidateOnStart.
        // The rule is added last, so it overrides Logging:LogLevel:Default from appsettings.
        // Category-specific rules (e.g. Microsoft.AspNetCore) still apply.
        services.AddOptions<LoggerFilterOptions>()
            .PostConfigure<StockroomOptionsSetup>((filter, setup) =>
            {
                var stockroom = new StockroomOptions();
                setup.Configure(stockroom);
                filter.MinLevel = stockroom.LogLevel;
                filter.Rules.Add(new LoggerFilterRule(providerName: null, categoryName: null, stockroom.LogLevel, filter: null));
            });

        return services;
    }
}
