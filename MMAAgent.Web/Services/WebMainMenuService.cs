using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebMainMenuService
{
    public Task<IReadOnlyList<SaveCardVm>> DetectSavesAsync()
    {
        var results = new List<SaveCardVm>();
        var roots = new List<string>();

        roots.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MMAAgent", "Saves"));

        roots.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MMAAgent"));

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.db", SearchOption.AllDirectories))
            {
                var info = new FileInfo(file);
                results.Add(new SaveCardVm(
                    file,
                    info.Name,
                    info.LastWriteTimeUtc,
                    info.Length));
            }
        }

        return Task.FromResult<IReadOnlyList<SaveCardVm>>(
            results.OrderByDescending(x => x.LastWriteTimeUtc).ToList());
    }

    public async Task RenameSaveAsync(string path, string newNameWithoutExtension)
    {
        if (string.IsNullOrWhiteSpace(newNameWithoutExtension))
            throw new InvalidOperationException("New save name is empty.");

        if (!File.Exists(path))
            throw new FileNotFoundException("Save not found.", path);

        var dir = Path.GetDirectoryName(path)!;
        var newPath = Path.Combine(dir, $"{newNameWithoutExtension.Trim()}.db");

        if (File.Exists(newPath))
            throw new InvalidOperationException("A save with that name already exists.");

        File.Move(path, newPath);
        await Task.CompletedTask;
    }

    public async Task DeleteSaveAsync(string path)
    {
        if (!File.Exists(path))
            return;

        File.Delete(path);
        await Task.CompletedTask;
    }
}
