# Country / Culture Data Guide

This guide documents how fighter origin data should scale in `MMAAgent`.

## Design split

- `Countries`
  - Defines the country itself.
  - Owns MMA quality and bias knobs such as `MmaLevel`, `WrestlingBias`, `StrikingBias`, `GrapplingBias`, and `FighterSpawnWeight`.
- `CountryCultureWeights`
  - Defines which cultures exist inside a country and with what weight.
  - This is the main source used to pick the fighter's culture.
- `FirstNameGroups` / `LastNameGroups`
  - Define reusable cultural pools of names.
  - This is the main source used to pick first names and last names.
- `FirstNameCountries` / `LastNameCountries`
  - Optional country-specific flavor overrides only.
  - They should not be the main source of names.

## Recommended flow

1. Pick a country from `Countries.FighterSpawnWeight`.
2. Pick a culture from `CountryCultureWeights`.
3. Pick the first name from `FirstNameGroups`.
4. Pick the last name from `LastNameGroups`.
5. Optionally use `FirstNameCountries` / `LastNameCountries` for country flavor.

## Adding a new country

Most of the time, adding a new country should only require:

1. Add a row to `Countries`.
2. Add one or more rows to `CountryCultureWeights`.

Only add `FirstNameCountries` / `LastNameCountries` if the country needs clear local flavor that is not already covered by the cultural pool.

If adding a country forces you to duplicate lots of names just to make it work, the data model is drifting in the wrong direction.

## Adding a new culture

When introducing a new `CulturalGroup`, add:

1. Enough first names in `FirstNames`.
2. Enough last names in `LastNames`.
3. Matching positive-weight rows in `FirstNameGroups`.
4. Matching positive-weight rows in `LastNameGroups`.
5. One or more `CountryCultureWeights` rows referencing that culture.

Avoid adding a culture to `CountryCultureWeights` before it has both a first-name pool and a last-name pool.

## Fallbacks

The generator still supports safe fallbacks:

- If a country has no `CountryCultureWeights`, it may fall back to `Countries.CulturalGroup`.
- If a culture has no names, it may fall back to global pools.

These fallbacks are safety nets, not the target state.

## Validation

Use the tools project to validate the web template DB:

```powershell
dotnet run --project C:\Users\agali\source\repos\MMAAgent\MMAAgent.Tools -- validate-country-data
```

If the local `dotnet` environment is acting up, there is also a Python fallback:

```powershell
python C:\Users\agali\source\repos\MMAAgent\MMAAgent.Tools\validate_country_data.py
```

Optional custom DB path:

```powershell
dotnet run --project C:\Users\agali\source\repos\MMAAgent\MMAAgent.Tools -- validate-country-data --db C:\path\to\MMA_Agent.db
```

```powershell
python C:\Users\agali\source\repos\MMAAgent\MMAAgent.Tools\validate_country_data.py --db C:\path\to\MMA_Agent.db
```

The validator checks:

- required tables exist
- `Fighters.CulturalGroup` exists
- countries with spawn weight have culture rows
- cultures used by countries have first-name and last-name pools
- override tables do not contain orphan rows

## Source of truth

Canonical seed data for the web template DB lives in:

- [country_culture_seed.sql](/C:/Users/agali/source/repos/MMAAgent/MMAAgent.Web/Assets/Database/country_culture_seed.sql)
- [MMA_Agent.db](/C:/Users/agali/source/repos/MMAAgent/MMAAgent.Web/Assets/Database/MMA_Agent.db)

`CareerSchemaPreparationService` should remain schema safety for old saves, not the main owner of the country/culture content.
