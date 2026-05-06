using MMAAgent.Application.Abstractions;

namespace MMAAgent.Web.Infrastructure;

public interface ISaveSessionContext
{
    string? CurrentSaveId { get; }
    string? CurrentPath { get; }
    string? CurrentOwnerUserId { get; }
    string? CurrentStorageKind { get; }
    string? CurrentStorageLocator { get; }
    string? CurrentSaveState { get; }
    string? CurrentTemplateSource { get; }
    string? CurrentBackendInstance { get; }
    void SetCurrent(string saveId, string path, string ownerUserId);
    void SetCurrent(SaveRecord record);
    void Clear();
}

public sealed class WebSaveSessionContext : ISaveSessionContext, ISavePathProvider
{
    public string? CurrentSaveId { get; private set; }
    public string? CurrentPath { get; private set; }
    public string? CurrentOwnerUserId { get; private set; }
    public string? CurrentStorageKind { get; private set; }
    public string? CurrentStorageLocator { get; private set; }
    public string? CurrentSaveState { get; private set; }
    public string? CurrentTemplateSource { get; private set; }
    public string? CurrentBackendInstance { get; private set; }

    public void SetCurrent(string saveId, string path, string ownerUserId)
    {
        CurrentSaveId = saveId;
        CurrentPath = path;
        CurrentOwnerUserId = ownerUserId;
        CurrentStorageKind = SaveStorageKinds.LocalSqliteFile;
        CurrentStorageLocator = path;
        CurrentSaveState = SaveLifecycleStates.Ready;
        CurrentTemplateSource = SaveTemplateSources.DefaultTemplateDb;
        CurrentBackendInstance = null;
    }

    public void SetCurrent(SaveRecord record)
    {
        CurrentSaveId = record.SaveId;
        CurrentPath = record.LocalPath ?? record.StorageLocator;
        CurrentOwnerUserId = record.OwnerUserId;
        CurrentStorageKind = record.StorageKind;
        CurrentStorageLocator = record.StorageLocator;
        CurrentSaveState = record.LifecycleState;
        CurrentTemplateSource = record.TemplateSource;
        CurrentBackendInstance = record.BackendInstance;
    }

    public void Clear()
    {
        CurrentSaveId = null;
        CurrentPath = null;
        CurrentOwnerUserId = null;
        CurrentStorageKind = null;
        CurrentStorageLocator = null;
        CurrentSaveState = null;
        CurrentTemplateSource = null;
        CurrentBackendInstance = null;
    }

    public void Set(string path)
    {
        CurrentPath = path;
        CurrentStorageLocator = path;
    }
}
