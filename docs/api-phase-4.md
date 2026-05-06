# API Phase 4

Phase 4 prepares the user identity side of the migration.

## Goal

Stop assuming that every caller is only "the local Windows user" and start supporting a user identity contract that can later be fed by Supabase Auth.

## What changed

- `IUserContextAccessor` now returns a richer identity shape
- the current implementation supports three modes:
  - `local`
  - `header`
  - `external`
- API response context now includes:
  - `isAuthenticated`
  - `authMode`
  - `provider`
  - `providerUserId`
- `/api/v1/session` now exposes the same user identity summary directly

## Why this matters

This lets us prepare the API and frontend contracts before real auth is connected.

Today we can still run locally with:

- local developer identity
- optional dev headers

Tomorrow the same contract can be backed by:

- Supabase JWT claims
- authenticated ownership checks
- provider-specific user IDs

## Dev testing

The smoke test now supports optional identity headers:

- `-UserId`
- `-DisplayName`
- `-AuthProvider`
- `-ProviderUserId`

That gives us an easy way to simulate authenticated callers before the real auth integration lands.

## Important limitation

This is still auth preparation only.

- no real JWT validation yet
- no Supabase SDK wiring yet
- no hard authorization checks by owner/provider yet

## Next logical step

The next phase should connect this identity contract to real save ownership enforcement and then to Supabase-backed authentication.
