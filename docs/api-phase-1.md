# API Phase 1

This first migration phase adds a small `/api/v1` surface on top of the current server app without changing the underlying game loop or save model.

## Goal

Prepare the project for a future split into:

- frontend app
- backend API
- authenticated user saves

while keeping the current local SQLite save workflow alive.

## What exists in phase 1

- `GET /api/v1/health`
- `GET /api/v1/session`
- `GET /api/v1/session/saves`
- `POST /api/v1/session/load/configured`
- `POST /api/v1/session/load/last`
- `POST /api/v1/session/load/path`
- `POST /api/v1/session/create`
- `GET /api/v1/dashboard`
- `GET /api/v1/roster`
- `GET /api/v1/world-feed`
- `GET /api/v1/prospects`
- `GET /api/v1/agent`

## Important limitation

This is still backed by the current in-process services and the active local SQLite save selected through `ISavePathProvider`.

That means:

- it is not multi-user yet
- it is not stateless yet
- it is not Supabase/Postgres ready yet
- it is an API preparation layer, not the final architecture

## Why this helps

It creates stable transport contracts for:

- session management
- dashboard reads
- roster reads
- world feed reads
- prospect pipeline reads
- agency profile reads

So future work can progressively move from Razor component service calls to external API consumption without redesigning these payloads again from scratch.
