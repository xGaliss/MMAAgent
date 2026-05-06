# Render Backend Deploy Prep

This backend is ready to run on Render as a Docker web service.

## Included runtime assumptions

- ASP.NET Core listens on the `PORT` environment variable provided by Render
- `ASPNETCORE_ENVIRONMENT=Production`
- save metadata and ownership live in Supabase Postgres
- remote SQLite snapshots also live in Supabase Postgres
- local SQLite files still exist, but Render free services have ephemeral disk

## Important note about persistence on Render free

Render free web services do not provide persistent disk suitable for durable local SQLite save files.

That means:

- remote snapshots in Supabase Postgres are still valuable
- but any local save materialization inside the container is ephemeral

For this architecture, Render is acceptable for:

- API validation
- auth validation
- save metadata ownership flows
- snapshot rehydration experiments

It is not ideal for durable long-lived local SQLite state unless you move fully away from local file dependence or use a provider with mounted persistent disk.

## Files added

- `render.yaml`
- `MMAAgent.Web/Dockerfile`
- `MMAAgent.Web/appsettings.Production.json`

## Environment variables to set in Render

Required:

- `SupabaseAuth__Issuer`
- `SupabaseAuth__Audience=authenticated`
- `SaveCatalog__PostgresConnectionString`
- `SaveCatalog__FallbackPostgresConnectionString`

Recommended:

- `SaveCatalog__BackendInstanceName=render`
- `Database__Path=/data/saves/bootstrap.db`
- `Database__SaveRootDirectory=/data/saves`

## Create the service

1. Push the repo to GitHub.
2. In Render, create a new **Web Service** from the repo.
3. Choose **Docker** runtime.
4. Let Render detect `render.yaml`, or configure the same values manually.

## Health check

Use:

`/api/v1/health`

## Recommendation

Render is good for getting the backend online quickly, but if you want durable on-disk SQLite behavior inside the app host, Fly with a volume is still the better fit later.
