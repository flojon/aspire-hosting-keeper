# Keeper Secrets Manager .NET Aspire Hosting Integration — Design

## Summary

A .NET Aspire hosting integration, `KoalaSoft.Aspire.Hosting.Keeper`, that lets an
AppHost resolve secrets stored in Keeper Secrets Manager and expose them to other
resources as `ParameterResource` values (env vars, connection strings, etc.),
without requiring the Keeper CLI or an interactive login session.

## Motivation

Aspire has no first-party secret-manager integration for Keeper. A comparable
community package exists for 1Password
(`Arkanis.Hosting.Extensions.1Password`), which shells out to the `op` CLI and
requires an interactively-signed-in local session — explicitly unsuitable for
CI/production. Keeper ships an official async .NET SDK
(`Keeper.SecretsManager`) with a token-bound, non-interactive auth model, so an
equivalent integration can avoid the CLI dependency and plausibly work beyond
local dev.

## Goals

- Resolve `keeper://UID/field/name` notation references (Keeper's own native
  notation syntax) into real secret values at AppHost startup.
- Support both an explicit builder API (`AddKeeperSecret`) and implicit
  config-scanning (bare `keeper://` strings in `IConfiguration`), mirroring the
  1Password extension's ergonomics.
- Batch all secret lookups for a given run into a single Keeper API call.
- Never resolve or leak secret values during `aspire publish` manifest
  generation.
- Fail fast and loudly on any resolution error (bad token, missing record,
  missing field) — no silent fallback to empty/blank values.

## Non-goals

- No custom UI/dashboard integration.
- No support for writing secrets back to Keeper.
- No first-class connection-string composition helpers (v1 is parameters +
  env vars only per approved design; can be added later without breaking
  changes).
- No automated CI test suite against a real Keeper vault (unit tests use a
  fake client/storage; the sample AppHost is a manual smoke test).

## Target Framework

.NET 8, Aspire 8.x (`Aspire.Hosting` 8.x line). Verify exact NuGet versions
against what's current at implementation time.

## Package / Project Layout

```
/src/KoalaSoft.Aspire.Hosting.Keeper/        # the integration itself
/samples/AppHost/                            # minimal sample AppHost exercising it
/test/KoalaSoft.Aspire.Hosting.Keeper.Tests/ # unit tests
```

## Architecture

The integration hooks into the AppHost's startup pipeline via an
`IDistributedApplicationLifecycleHook` that runs once, before dependent
resources start (on `BeforeStartEvent` / equivalent hook point). At that time
it:

1. Collects every `KeeperParameterReferenceAnnotation` attached to a
   `ParameterResource` (from explicit `AddKeeperSecret` calls).
2. Scans `IConfiguration` for bare `keeper://...` string values and treats
   each as an implicit reference.
3. Extracts the distinct set of record UIDs referenced across both sources.
4. Calls `KeeperSecretResolver` once to fetch all of them in a single Keeper
   SDK `GetSecrets` call.
5. Resolves each individual notation string against the cached result (via
   the SDK's own notation parser) and writes the value onto its
   `ParameterResource` (explicit) or back into configuration (implicit),
   before any consuming resource starts.

At publish time, the hook does not run resolution — publishing tools may
inspect the annotation to know a value is secret-sourced, without triggering
an actual Keeper API call or embedding the value in the manifest.

## Components

### `KeeperSecretsManagerOptions`

Holds:
- `IKeyValueStorage Storage` — defaults to `LocalConfigStorage` pointed at a
  configurable path (default `~/.keeper/aspire-config.json`).
- `string? OneTimeToken` — optional, used only on first run to bootstrap the
  local encrypted config; not needed on subsequent runs once the config file
  exists.

### `UseKeeper`

```csharp
IDistributedApplicationBuilder UseKeeper(
    this IDistributedApplicationBuilder builder,
    Action<KeeperSecretsManagerOptions>? configure = null)
```

Registers options and the lifecycle hook. Called once in the AppHost's
`Program.cs`. Fails fast (throws) if the resulting storage/token state is
invalid (e.g., no config file exists and no one-time token was supplied).

### `AddKeeperSecret`

```csharp
IResourceBuilder<ParameterResource> AddKeeperSecret(
    this IDistributedApplicationBuilder builder,
    string name,
    string notation)
```

Creates a `ParameterResource` named `name` with a
`KeeperParameterReferenceAnnotation(notation)` attached. Its value stays
unresolved until the lifecycle hook runs.

### `KeeperParameterReferenceAnnotation`

An `IResourceAnnotation` carrying the raw `keeper://...` notation string for
a given parameter. Consulted by the lifecycle hook and inspectable by
publishing tools.

### `KeeperSecretResolver`

Thin wrapper around `SecretsManagerClient`:
- `Task<IReadOnlyDictionary<string, string>> ResolveAsync(IEnumerable<string> notations, KeeperSecretsManagerOptions options)`
- Internally: extracts UIDs from the notations, calls
  `SecretsManagerClient.GetSecrets(options, uids)` once, then answers each
  notation via the SDK's `Notation.GetValue`-equivalent against the cached
  records.

## Data Flow Diagram

```
AppHost Program.cs
  ├─ builder.UseKeeper(...)              → registers options + hook
  ├─ builder.AddKeeperSecret("db-pw", "keeper://UID/field/password")
  └─ appsettings.json: "Smtp:Password": "keeper://UID2/field/password"

BeforeStartEvent (lifecycle hook)
  ├─ collect annotations + config-scanned strings
  ├─ extract distinct UIDs → {UID, UID2}
  ├─ KeeperSecretResolver.ResolveAsync(...) → one GetSecrets call
  ├─ set ParameterResource("db-pw").Value
  └─ write IConfiguration["Smtp:Password"]

Other resources
  └─ .WithEnvironment("DB_PASSWORD", dbPwParam)  → receives resolved value
```

## Error Handling

- Invalid/missing local config and no one-time token at `UseKeeper()`
  registration time → throw immediately with a clear message (don't allow the
  app to start with unresolved secrets pending).
- A referenced UID or field not found during resolution → throw, naming the
  specific `keeper://` reference that failed.
- No fallback to empty string / null for any resolution failure.

## Testing Strategy

- Unit tests (in `KoalaSoft.Aspire.Hosting.Keeper.Tests`) covering:
  - Notation string parsing / UID extraction.
  - Batching logic (N notations across M UIDs → 1 resolver call).
  - Error propagation for missing UID/field and missing token/config.
  - Config-scan detection of bare `keeper://` strings.
  - All tests use a fake `SecretsManagerClient`/`IKeyValueStorage` — no real
    Keeper vault or network access required.
- The sample AppHost project is a manual integration smoke test run against a
  real (test) Keeper vault; not part of automated CI in v1.

## Open Questions / Risks for Implementation Phase

- Exact current Aspire 8.x API names for lifecycle hooks and
  `ParameterResource` value-setting need to be verified against the actual
  SDK at implementation time (Aspire's extensibility surface has shifted
  across versions).
- Exact current `Keeper.SecretsManager` NuGet package API (method names for
  batched `GetSecrets` by UID list, notation parsing helper) needs
  verification against the latest SDK version at implementation time.
