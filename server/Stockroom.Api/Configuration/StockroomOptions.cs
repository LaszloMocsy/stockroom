namespace Stockroom.Api.Configuration;

/// <summary>
/// Deployment settings read from <c>STOCKROOM_*</c> environment variables (spec 12.2).
/// Values are validated at startup, so consumers can rely on them being present and well formed.
/// </summary>
public sealed class StockroomOptions
{
    public const string DatabaseUrlKey = "STOCKROOM_DATABASE_URL";
    public const string PublicUrlKey = "STOCKROOM_PUBLIC_URL";
    public const string AllowedCorsOriginsKey = "STOCKROOM_ALLOWED_CORS_ORIGINS";
    public const string LogLevelKey = "STOCKROOM_LOG_LEVEL";

    /// <summary>PostgreSQL connection string. Required.</summary>
    public string DatabaseUrl { get; set; } = string.Empty;

    /// <summary>Absolute http(s) URL clients use to reach this server. Required.</summary>
    public Uri PublicUrl { get; set; } = null!;

    /// <summary>Origins allowed to make cross-origin requests, e.g. <c>https://app.example.com</c>. Empty by default.</summary>
    public IReadOnlyList<string> AllowedCorsOrigins { get; set; } = [];

    /// <summary>Default minimum log level. <see cref="LogLevel.Information"/> by default.</summary>
    public LogLevel LogLevel { get; set; } = LogLevel.Information;
}
