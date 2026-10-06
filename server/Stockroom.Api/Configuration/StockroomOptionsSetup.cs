using Microsoft.Extensions.Options;

namespace Stockroom.Api.Configuration;

/// <summary>
/// Binds <see cref="StockroomOptions"/> from configuration and reports every invalid or missing
/// setting by its environment variable name.
/// </summary>
internal sealed class StockroomOptionsSetup(IConfiguration configuration)
    : IConfigureOptions<StockroomOptions>, IValidateOptions<StockroomOptions>
{
    public void Configure(StockroomOptions options) => Bind(options, errors: []);

    public ValidateOptionsResult Validate(string? name, StockroomOptions options)
    {
        var errors = new List<string>();
        Bind(new StockroomOptions(), errors);
        return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
    }

    private void Bind(StockroomOptions options, List<string> errors)
    {
        var databaseUrl = Read(StockroomOptions.DatabaseUrlKey);
        if (databaseUrl is null)
        {
            errors.Add($"{StockroomOptions.DatabaseUrlKey} is required: set it to the PostgreSQL connection string.");
        }
        else
        {
            options.DatabaseUrl = databaseUrl;
        }

        var publicUrl = Read(StockroomOptions.PublicUrlKey);
        if (publicUrl is null)
        {
            errors.Add($"{StockroomOptions.PublicUrlKey} is required: set it to the URL clients use to reach this server, e.g. https://stock.example.com.");
        }
        else if (TryParseHttpUrl(publicUrl, out var uri))
        {
            options.PublicUrl = uri;
        }
        else
        {
            errors.Add($"{StockroomOptions.PublicUrlKey} must be an absolute http or https URL, got '{publicUrl}'.");
        }

        var origins = new List<string>();
        foreach (var origin in (Read(StockroomOptions.AllowedCorsOriginsKey) ?? string.Empty)
                     .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryParseHttpUrl(origin, out var uri) && uri.PathAndQuery == "/" && uri.Fragment.Length == 0)
            {
                origins.Add(uri.GetLeftPart(UriPartial.Authority));
            }
            else
            {
                errors.Add($"{StockroomOptions.AllowedCorsOriginsKey} must be a comma-separated list of origins such as https://app.example.com (scheme, host, optional port; no path), got '{origin}'.");
            }
        }

        options.AllowedCorsOrigins = origins;

        var logLevel = Read(StockroomOptions.LogLevelKey);
        if (logLevel is not null)
        {
            // Names only: Enum.TryParse would also accept arbitrary numbers.
            var match = Enum.GetNames<LogLevel>().FirstOrDefault(n => n.Equals(logLevel, StringComparison.OrdinalIgnoreCase));
            if (match is null)
            {
                errors.Add($"{StockroomOptions.LogLevelKey} must be one of {string.Join(", ", Enum.GetNames<LogLevel>())}, got '{logLevel}'.");
            }
            else
            {
                options.LogLevel = Enum.Parse<LogLevel>(match);
            }
        }
    }

    private string? Read(string key)
    {
        var value = configuration[key]?.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }

    private static bool TryParseHttpUrl(string value, out Uri uri) =>
        Uri.TryCreate(value, UriKind.Absolute, out uri!) && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);
}
