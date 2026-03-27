using MMAAgent.Application.Abstractions;

namespace MMAAgent.Web.Infrastructure;

public sealed class WebSavePathProvider : ISavePathProvider
{
    public string? CurrentPath { get; private set; }

    public void Set(string path)
    {
        CurrentPath = path;
    }
}
