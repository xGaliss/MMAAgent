# API Phase 6

Phase 6 prepares the auth bridge toward a real provider such as Supabase Auth.

## Goal

Move user identity handling away from hardcoded local/header assumptions and toward a configurable provider-aware contract.

## What changed

- `AuthBridgeOptions` now configures:
  - dev header names
  - local provider name
  - header provider name
  - Supabase issuer hints
- `HttpAwareUserContextAccessor` now reads those options instead of hardcoding auth bridge behavior
- external identities normalize provider names, including automatic `supabase` detection from issuer hints
- new endpoint:
  - `GET /api/v1/auth/me`

## Why this matters

This gives us a clean seam between:

- local desktop testing
- header-based simulation
- future JWT/claims-based auth

So when Supabase JWT validation arrives, we should be able to plug it into the same identity contract instead of rewriting session and ownership again.

## Dev testing

You can still simulate identities with headers, and now also inspect them directly:

```powershell
Invoke-RestMethod `
  -Method Get `
  -Uri http://127.0.0.1:5116/api/v1/auth/me `
  -Headers @{
    "X-MMA-User-Id" = "auth:test-user"
    "X-MMA-Display-Name" = "Test User"
    "X-MMA-Auth-Provider" = "supabase-dev"
    "X-MMA-Provider-User-Id" = "supabase|test-user"
  }
```

## Important limitation

This is still bridge prep only.

- no real JWT validation yet
- no Supabase SDK integration yet
- no authorization middleware yet

## Next logical step

The next phase should connect this identity bridge to real authenticated claims and then enforce save ownership from provider-backed users end to end.

## Follow-up

Phase 7 adds an actual authentication pipeline seam on top of this bridge so the app starts resolving `HttpContext.User` through an auth handler instead of relying only on direct header reads.
