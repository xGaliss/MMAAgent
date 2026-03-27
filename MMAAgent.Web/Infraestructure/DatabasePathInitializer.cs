using Microsoft.Extensions.Options;
using MMAAgent.Application.Abstractions;

namespace MMAAgent.Web.Infrastructure;

public interface IDatabasePathInitializer
{
    void Initialize();
}

public sealed class DatabasePathInitializer : IDatabasePathInitializer
{
    private readonly IOptions<DatabaseOptions> _options;
    private readonly ISavePathProvider _savePathProvider;

    public DatabasePathInitializer(
        IOptions<DatabaseOptions> options,
        ISavePathProvider savePathProvider)
    {
        _options = options;
        _savePathProvider = savePathProvider;
    }

    public void Initialize()
    {
        var path = _options.Value.Path?.Trim();

        if (string.IsNullOrWhiteSpace(path))
        {
            throw new InvalidOperationException(
                "Database:Path no está configurado en appsettings.json.");
        }

        _savePathProvider.Set(path);
    }
}
