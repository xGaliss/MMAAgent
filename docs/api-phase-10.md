# API Phase 10

Phase 10 introduces remote persistence for the actual save file while keeping the game state SQLite-based locally.

## Goal

Keep the current simulation database as SQLite for now, but persist the save payload itself in backend-managed Postgres so the save is no longer only a local disk concern.

## What changed

- new snapshot persistence abstraction:
  - `ISaveSnapshotStore`
- Postgres implementation:
  - `PostgresSaveSnapshotStore`
- save persistence coordinator:
  - `ISavePersistenceService`
  - `SavePersistenceService`
- remote snapshot table:
  - `save_snapshots`
- manual API sync endpoint:
  - `POST /api/v1/session/persist`

## Runtime behavior

- creating a new game now uploads the save snapshot to the backend when remote persistence is enabled
- loading a save now tries to materialize the local `.db` from backend snapshot data if the file is missing
- several important mutation flows now trigger a save sync:
  - time advance
  - inbox mutations / decisions
  - fighter actions
  - division actions
  - agent staffing / investment adjustments

## Why this matters

This is the first phase where the save content itself is no longer only a local file.

That means we now have:

- remote save metadata
- remote save ownership
- remote save payload snapshot

while still avoiding a risky full migration of the gameplay database away from SQLite.

## Schema

`docs/save-catalog-postgres.sql` now includes:

- `save_records`
- `save_ownership`
- `save_snapshots`

## Current limitation

This is still snapshot-based persistence, not a live relational rewrite of the game state.

So the backend currently stores the SQLite save as a persisted blob and rehydrates it when needed.

## Connection strategy

The remote save catalog and snapshot store now support two Postgres connection strings:

- `SaveCatalog:PostgresConnectionString`
- `SaveCatalog:FallbackPostgresConnectionString`

The backend tries the primary connection first and caches the working choice. If that connection fails, it automatically retries using the fallback.

This is useful when:

- production should prefer a direct Supabase connection
- local development needs to fall back to a Session Pooler connection

The backend now accepts both:

- classic Npgsql connection strings (`Host=...;Port=...;Database=...;Username=...;Password=...`)
- URI-style Supabase strings (`postgresql://user:password@host:5432/postgres`)
