# API Phase 8

Phase 8 wires the bridge into a JWT-ready authentication pipeline.

## Goal

Allow the backend to accept real bearer tokens while preserving the current local/header development flow.

## What changed

- hybrid auth scheme:
  - `MmaHybridAuth`
- request routing:
  - `Authorization: Bearer ...` -> JWT bearer pipeline
  - no bearer token -> development bridge scheme
- new options section:
  - `SupabaseAuth`
- claims transformation:
  - normalizes external identities into the same `mma_user_id` contract used by the rest of the app

## Why this matters

This is the step that makes the auth model realistic:

- local testing still works
- header simulation still works
- real JWT-based identity now has a natural place to plug in

So the next Supabase step is no longer "invent auth", but simply "feed valid Supabase JWT into the bearer pipeline".

## Current limitation

This does **not** validate Supabase in production-ready form yet unless you provide real issuer/audience settings and a real bearer token.

It is the structural bridge for that final integration.

## Config

`appsettings.json` now includes:

- `SupabaseAuth:Issuer`
- `SupabaseAuth:Audience`
- `SupabaseAuth:Audiences`
- `SupabaseAuth:RequireHttpsMetadata`
- `SupabaseAuth:ValidateIssuer`
- `SupabaseAuth:ValidateAudience`
- `SupabaseAuth:ValidateLifetime`

## Smoke test support

The smoke script now accepts:

- `-BearerToken`

so we can test the bearer path without rewriting the script.
