using MMAAgent.Web.Infrastructure;
using MMAAgent.Web.Models;
using Microsoft.Extensions.Options;

namespace MMAAgent.Web.Services;

public sealed class WebMainMenuService
{
    private readonly ISaveCatalogService _saveCatalogService;
    private readonly ISavePersistenceService _savePersistenceService;
    private readonly IUserContextAccessor _userContextAccessor;
    private readonly ISaveSessionContext _saveSessionContext;
    private readonly DatabaseOptions _databaseOptions;

    public WebMainMenuService(
        ISaveCatalogService saveCatalogService,
        ISavePersistenceService savePersistenceService,
        IUserContextAccessor userContextAccessor,
        ISaveSessionContext saveSessionContext,
        IOptions<DatabaseOptions> databaseOptions)
    {
        _saveCatalogService = saveCatalogService;
        _savePersistenceService = savePersistenceService;
        _userContextAccessor = userContextAccessor;
        _saveSessionContext = saveSessionContext;
        _databaseOptions = databaseOptions.Value;
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

        if (!string.IsNullOrWhiteSpace(_databaseOptions.SaveRootDirectory))
            roots.Add(Path.GetFullPath(_databaseOptions.SaveRootDirectory.Trim()));

        foreach (var root in roots
                     .Where(Directory.Exists)
                     .Distinct(StringComparer.OrdinalIgnoreCase))
        {
            foreach (var file in Directory.EnumerateFiles(root, "*.db", SearchOption.AllDirectories))
            {
                var existing = await _saveCatalogService.GetByLocalPathAsync(file);
                if (existing is null)
                {
                    await _saveCatalogService.RegisterOrUpdateLocalAsync(file, ownerUserId);
                }
                else
                {
                    await _saveCatalogService.RegisterOrUpdateLocalAsync(
                        file,
                        existing.OwnerUserId,
                        existing.DisplayName,
                        existing.TemplateSource,
                        markOpened: false);
                }
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

        await EnsureOwnedAsync(path);
        File.Move(path, newPath);
        await _saveCatalogService.RenameLocalPathAsync(path, newPath, _userContextAccessor.CurrentUserId);
    }

    public async Task DeleteSaveAsync(string path)
    {
        var existing = await _saveCatalogService.GetByLocalPathAsync(path);
        if (existing is null && !File.Exists(path))
            return;

        await EnsureOwnedAsync(path);
        if (File.Exists(path))
            File.Delete(path);

        if (existing is not null)
            await _savePersistenceService.DeletePersistedSaveAsync(existing);
        await _saveCatalogService.RemoveByLocalPathAsync(path);
    }

    private async Task EnsureOwnedAsync(string path)
    {
        var existing = await _saveCatalogService.GetByLocalPathAsync(path);
        if (existing is null)
            return;

        if (!string.Equals(existing.OwnerUserId, _userContextAccessor.CurrentUserId, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException("This save belongs to another owner and cannot be modified from the current session.");
    }
}
