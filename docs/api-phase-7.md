# API Phase 7

Phase 7 introduces a real authentication pipeline seam for the bridge we prepared earlier.

## Goal

Move identity resolution onto ASP.NET Core authentication primitives so the app starts treating authenticated callers as claims-based users instead of only reading ad hoc headers.

## What changed

- `UseAuthentication()` and `UseAuthorization()` are now part of the request pipeline
- custom auth scheme:
  - `MmaAuthBridge`
- custom handler:
  - `AuthBridgeAuthenticationHandler`
- development auth headers now create an authenticated `ClaimsPrincipal`
- `HttpAwareUserContextAccessor` now prefers claims that come from the auth pipeline
- logical user id is preserved through a dedicated claim:
  - `mma_user_id`

## Why this matters

This is the first point where the app is truly shaped like an authenticated backend.

That means the future Supabase work can focus on replacing the bridge authentication handler with real JWT validation instead of rebuilding session and ownership around a new auth model.

## Current behavior

- no auth headers:
  - local fallback identity
- auth headers present:
  - authenticated principal created by the bridge auth handler
- future real JWT:
  - expected to plug into the same `HttpContext.User` flow

## Important limitation

This is still not real Supabase validation.

- no JWT signature validation yet
- no token audience / issuer enforcement yet
- still intended as bridge infrastructure

## Quick test

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

You should now see that the caller is authenticated and resolved through the bridge identity contract.
