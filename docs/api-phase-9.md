# API Phase 9

Phase 9 starts the shift from "development-friendly bridge" to "real backend boundary".

## Goal

Do two things in parallel:

- make save operations prefer real external auth when Supabase auth is enabled
- move save catalog metadata toward a Postgres-friendly shape, while the actual game state still lives in SQLite

## What changed

- new config section:
  - `ApiSecurity`
- save operations can now require `external` auth mode:
  - `GET /api/v1/session/saves`
  - `POST /api/v1/session/load/*`
  - `POST /api/v1/session/create`
- new config section:
  - `SaveCatalog`
- new optional Postgres-backed metadata store:
  - `PostgresSaveCatalogService`
- new configurable wrapper:
  - `ConfiguredSaveCatalogService`
- local JSON catalog remains available as:
  - fallback
  - migration bridge
  - optional mirror

## Why this matters

This is the first step where the backend starts behaving like a multi-user service instead of only a local desktop app.

The important split is now explicit:

- save **content**: still SQLite for now
- save **catalog / ownership metadata**: can move independently toward Supabase Postgres

That lets us migrate safely without forcing the whole simulation database to move in one shot.

## New config

`appsettings.json` now includes:

- `ApiSecurity:RequireExternalAuthForSaveOperations`
- `ApiSecurity:AllowDevelopmentBypassForSaveOperations`
- `SaveCatalog:Provider`
- `SaveCatalog:MirrorLocalJson`
- `SaveCatalog:Schema`
- `SaveCatalog:PostgresConnectionString`
- `SaveCatalog:BackendInstanceName`

## Postgres schema

A starter schema script now lives at:

- `docs/save-catalog-postgres.sql`

It creates:

- `save_records`
- `save_ownership`

inside the configured schema.

## Current limitation

The metadata catalog can move to Postgres before the game state does, but the save payload is still a local SQLite file.

That is intentional for this phase.
