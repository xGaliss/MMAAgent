# API Phase 3

Phase 3 reshapes local save tracking into a catalog that already looks like a future backend save layer.

## Goal

Stop modeling saves as "a path plus a few timestamps" and start modeling them as logical save records with:

- a stable `saveId`
- an `ownerUserId`
- a `storageKind`
- a `lifecycleState`
- a `templateSource`
- a `storageLocator`

The local SQLite file still exists, but it now sits behind a save catalog instead of being the save model itself.

## What changed

- `ISaveCatalogService` replaces the thinner registry abstraction
- local saves are stored as `SaveRecord`
- catalog records now carry:
  - `StorageKind`
  - `LifecycleState`
  - `TemplateSource`
  - `StorageLocator`
  - `BackendInstance`
  - `LocalPath`
- the local JSON persistence migrated from `save-registry.json` semantics to `save-catalog.json`
- legacy registry data is still read and mapped forward automatically
- session context now carries storage metadata, not just `saveId` and `path`
- API session/save responses now expose save storage metadata

## Why this matters

This is the bridge between:

- today's local desktop flow
- tomorrow's Supabase/Postgres save ownership model

When we later move to a remote save backend, the API and frontend will already understand that a save has:

- identity
- ownership
- storage backend
- readiness state

instead of assuming "a save is a local file path".

## Important limitation

This is still local under the hood.

- `storageKind` is currently `local-sqlite-file`
- `storageLocator` still resolves to a filesystem path
- repositories still read the active SQLite DB through the current path provider

So phase 3 is still preparation, not the storage migration itself.

## Next logical step

The next backend-oriented pass should introduce a more explicit persisted save record model for:

- remote ownership
- save provisioning state
- future Postgres-backed game storage
- Supabase-authenticated users
