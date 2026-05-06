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
            var root = ResolveSaveRoot();
            Directory.CreateDirectory(root);
            path = Path.Combine(root, "bootstrap.db");
        }

        _savePathProvider.Set(path);
    }

    private string ResolveSaveRoot()
    {
        var configured = _options.Value.SaveRootDirectory?.Trim();
        if (!string.IsNullOrWhiteSpace(configured))
            return configured;

        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MMAAgent",
            "Saves");
    }
}
