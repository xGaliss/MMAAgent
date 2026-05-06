# API Phase 5

Phase 5 turns save ownership from metadata into an enforced rule.

## Goal

Make sure a caller cannot silently operate on a save owned by another user identity.

## What changed

- loading a save by `saveId` now checks ownership
- loading a save by local path now checks whether that path is already owned by someone else
- the local save scan no longer reassigns ownership just because a file was found on disk
- rename and delete operations now respect ownership
- session bootstrap treats a save as inactive when the current caller does not own it
- load endpoints return `403` instead of failing ambiguously when ownership is violated

## Why this matters

This is the first pass where ownership actually influences behavior.

That gives us a much better bridge toward:

- authenticated users
- remote save catalogs
- Supabase-backed ownership checks

## Dev testing idea

You can simulate two different callers with the smoke test headers:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\api-smoke-test.ps1 -CreateTestSave -UserId "auth:one" -DisplayName "User One"
```

Then try the same save from another user identity:

```powershell
powershell -ExecutionPolicy Bypass -File .\tools\api-smoke-test.ps1 -LoadSaveId "<save-id>" -UserId "auth:two" -DisplayName "User Two"
```

The second caller should now hit ownership enforcement instead of loading the save.
