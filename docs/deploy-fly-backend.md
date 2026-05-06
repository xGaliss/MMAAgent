# Fly.io Backend Deploy Prep

This backend is now prepared to run as a persistent ASP.NET API on Fly.io.

## Included runtime assumptions

- ASP.NET Core runs on port `8080`
- TLS is terminated by Fly
- local SQLite save files live on the mounted Fly volume at `/data/saves`
- save metadata and ownership live in Supabase Postgres
- remote SQLite snapshots can also live in Supabase Postgres

## Files added

- `fly.toml`
- `MMAAgent.Web/Dockerfile`
- `MMAAgent.Web/appsettings.Production.json`

## Required Fly secrets

Set these with `fly secrets set`:

```powershell
fly secrets set `
  SupabaseAuth__Issuer="https://YOUR_PROJECT.supabase.co/auth/v1" `
  SupabaseAuth__Audience="authenticated" `
  SaveCatalog__PostgresConnectionString="YOUR_DIRECT_CONNECTION_STRING" `
  SaveCatalog__FallbackPostgresConnectionString="YOUR_SESSION_POOLER_CONNECTION_STRING"
```

If the base `appsettings.json` is not the final production project config, also set:

```powershell
fly secrets set `
  SupabaseAuth__Enabled="true"
```

## Volume

Create a Fly volume before deploy:

```powershell
fly volumes create mmaagent_data --region mad --size 5
```

## First deploy

```powershell
fly launch --no-deploy
fly deploy
```

## Notes

- production relies on forwarded headers and Fly HTTPS termination
- app-level HTTPS redirection is disabled outside development
- direct Supabase Postgres connection is preferred
- session pooler is supported as automatic fallback
