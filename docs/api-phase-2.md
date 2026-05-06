# API Phase 2

Phase 2 introduces a logical save identity layer on top of the existing local SQLite workflow.

## Goal

Stop treating the active save as "just a file path" and start treating it as:

- a logical `saveId`
- owned by a logical `ownerUserId`
- resolved to a physical file path only at the infrastructure edge

## What changed

- local user context abstraction
- save session context abstraction
- persistent local save registry
- save detection and loading can now work through `saveId`
- API session responses now expose logical save identity
- key read endpoints can now carry save-aware context instead of assuming the client already knows it

## Why this matters

This keeps the current local desktop workflow intact while preparing the next migration steps:

- Supabase Auth user identity
- save ownership by authenticated user
- moving from local file saves to logical save records in a database

## Important limitation

This is still a local single-user implementation under the hood.

- `ownerUserId` is local/dev flavored
- the physical save still lives as a `.db` file
- repositories still open SQLite through the current path provider

So this phase is about identity separation, not storage migration yet.

## Phase 2.1

The follow-up pass adds a shared response context pattern:

- `ApiEnvelope<T>`
- `ApiResponseContext`
- `ApiUserContextSummary`
- `ApiSaveContextSummary`

This is now used by the main read endpoints so a future frontend can always know:

- which logical user is calling
- which logical save is active
- which physical local save path currently backs that session

without reconstructing that state from separate calls.
