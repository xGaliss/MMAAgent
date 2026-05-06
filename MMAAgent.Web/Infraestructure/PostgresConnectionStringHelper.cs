using Npgsql;

namespace MMAAgent.Web.Infrastructure;

internal static class PostgresConnectionStringHelper
{
    public static string Normalize(string rawConnectionString)
    {
        if (string.IsNullOrWhiteSpace(rawConnectionString))
            throw new InvalidOperationException("Postgres connection string is empty.");

        var trimmed = rawConnectionString.Trim();
        if (!trimmed.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase)
            && !trimmed.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            return trimmed;
        }

        if (!Uri.TryCreate(trimmed, UriKind.Absolute, out var uri))
            throw new InvalidOperationException("Postgres URI connection string is invalid.");

        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.IsDefaultPort ? 5432 : uri.Port,
            Database = uri.AbsolutePath.Trim('/'),
            Username = Uri.UnescapeDataString(uri.UserInfo.Split(':', 2)[0]),
            SslMode = SslMode.Require,
            TrustServerCertificate = true
        };

        var password = ExtractPassword(uri);
        if (!string.IsNullOrEmpty(password))
            builder.Password = password;

        ApplyQueryString(uri, builder);
        return builder.ConnectionString;
    }

    private static string? ExtractPassword(Uri uri)
    {
        if (string.IsNullOrWhiteSpace(uri.UserInfo))
            return null;

        var parts = uri.UserInfo.Split(':', 2);
        if (parts.Length < 2)
            return null;

        return Uri.UnescapeDataString(parts[1]);
    }

    private static void ApplyQueryString(Uri uri, NpgsqlConnectionStringBuilder builder)
    {
        var query = uri.Query;
        if (string.IsNullOrWhiteSpace(query))
            return;

        foreach (var pair in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            var key = Uri.UnescapeDataString(parts[0]);
            var value = parts.Length > 1 ? Uri.UnescapeDataString(parts[1]) : string.Empty;

            if (string.IsNullOrWhiteSpace(key))
                continue;

            switch (key.Trim().ToLowerInvariant())
            {
                case "sslmode":
                    if (Enum.TryParse<SslMode>(value, true, out var sslMode))
                        builder.SslMode = sslMode;
                    break;
                case "trustservercertificate":
                    if (bool.TryParse(value, out var trustServerCertificate))
                        builder.TrustServerCertificate = trustServerCertificate;
                    break;
                case "pooling":
                    if (bool.TryParse(value, out var pooling))
                        builder.Pooling = pooling;
                    break;
                case "commandtimeout":
                    if (int.TryParse(value, out var commandTimeout))
                        builder.CommandTimeout = commandTimeout;
                    break;
                case "timeout":
                    if (int.TryParse(value, out var timeout))
                        builder.Timeout = timeout;
                    break;
                case "searchpath":
                    builder.SearchPath = value;
                    break;
            }
        }
    }
}
