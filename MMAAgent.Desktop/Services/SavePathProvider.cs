using MMAAgent.Application.Abstractions;

namespace MMAAgent.Desktop.Services
{
    public sealed class SavePathProvider : ISavePathProvider
    {
        public string? CurrentPath { get; private set; }

        public void Set(string path)
        {
            CurrentPath = path;
        }
    }
}