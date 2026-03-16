namespace MMAAgent.Application.Abstractions
{
    public interface ISavePathProvider
    {
        string? CurrentPath { get; }
        void Set(string path);
    }
}