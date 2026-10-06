# Configuration

Stockroom is configured through environment variables prefixed `STOCKROOM_`. Settings are validated at startup. If any are missing or invalid, the server prints every problem and exits with code 1 instead of starting:

```txt
Stockroom cannot start because its configuration is invalid:
  - STOCKROOM_DATABASE_URL is required: set it to the PostgreSQL connection string.
  - STOCKROOM_PUBLIC_URL is required: set it to the URL clients use to reach this server, e.g. https://stock.example.com.
```

Runtime-changeable preferences (such as the negative-stock policy) live in the database, not in environment variables. See [Section 12.2 of the specification](../SPECIFICATION.md#122-configuration).

## Settings

| Variable                         | Required | Default       | Description                                                                                                                  |
| -------------------------------- | :------: | ------------- | ---------------------------------------------------------------------------------------------------------------------------- |
| `STOCKROOM_DATABASE_URL`         |   yes    | —             | PostgreSQL connection string, for example `Host=db;Database=stockroom;Username=stockroom;Password=secret`.                   |
| `STOCKROOM_PUBLIC_URL`           |   yes    | —             | Absolute `http` or `https` URL that clients use to reach this server, for example `https://stock.example.com`.               |
| `STOCKROOM_ALLOWED_CORS_ORIGINS` |    no    | empty         | Comma-separated origins allowed to call the API from a browser, for example `https://app.example.com,http://localhost:5173`. |
| `STOCKROOM_LOG_LEVEL`            |    no    | `Information` | Default minimum log level: `Trace`, `Debug`, `Information`, `Warning`, `Error`, `Critical`, or `None` (case-insensitive).    |

### Notes

- **CORS origins** are a scheme, host, and optional port, with no path. A trailing `/` is ignored. Leave the list empty when the web app is served behind the same reverse proxy as the API, which avoids CORS entirely.
- **Log level** sets the default only. Noisy framework categories such as `Microsoft.AspNetCore` stay at `Warning`.
- Treat the database URL as a secret: it contains the password. Never commit it.

## Local development

In the `Development` environment, `appsettings.Development.json` supplies defaults for the two required settings, so `dotnet run` works without setting any variables. Environment variables take precedence over the file.

## Adding a setting

Every new `STOCKROOM_*` variable gets a row in the table above in the same commit that introduces it.
