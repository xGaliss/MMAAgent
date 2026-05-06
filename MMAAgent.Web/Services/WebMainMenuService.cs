using MMAAgent.Web.Infrastructure;
using MMAAgent.Web.Models;

namespace MMAAgent.Web.Services;

public sealed class WebMainMenuService
{
    private readonly ISaveCatalogService _saveCatalogService;
    private readonly IUserContextAccessor _userContextAccessor;
    private readonly ISaveSessionContext _saveSessionContext;

    public WebMainMenuService(
        ISaveCatalogService saveCatalogService,
        IUserContextAccessor userContextAccessor,
        ISaveSessionContext saveSessionContext)
    {
        _saveCatalogService = saveCatalogService;
        _userContextAccessor = userContextAccessor;
        _saveSessionContext = saveSessionContext;
    }

    public async Task<IReadOnlyList<SaveCardVm>> DetectSavesAsync()
    {
        var ownerUserId = _userContextAccessor.CurrentUserId;
        var roots = new List<string>();

        roots.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "MMAAgent", "Saves"));

        roots.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "MMAAgent", "Saves"));

        roots.Add(Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            "MMAAgent"));

        foreach (var root in roots.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.db", SearchOption.AllDirectories))
            {
                await _saveCatalogService.RegisterOrUpdateLocalAsync(file, ownerUserId);
            }
        }

        var entries = await _saveCatalogService.ListByOwnerAsync(ownerUserId);

        return entries
            .Select(x => new SaveCardVm(
                x.LocalPath ?? x.StorageLocator,
                x.FileName,
                x.LastWriteTimeUtc,
                x.FileSizeBytes,
                x.SaveId,
                x.OwnerUserId,
                string.Equals(x.SaveId, _saveSessionContext.CurrentSaveId, StringComparison.OrdinalIgnoreCase),
                x.DisplayName,
                x.StorageKind,
                x.LifecycleState,
                x.TemplateSource))
            .ToArray();
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
        await _saveCatalogService.RenameLocalPathAsync(path, newPath, _userContextAccessor.CurrentUserId);
    }

    public async Task DeleteSaveAsync(string path)
    {
        if (!File.Exists(path))
            return;

        File.Delete(path);
        await _saveCatalogService.RemoveByLocalPathAsync(path);
    }
}
