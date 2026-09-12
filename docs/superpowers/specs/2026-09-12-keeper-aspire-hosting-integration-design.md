# Keeper Secrets Manager .NET Aspire Hosting Integration — Design

## Summary

A .NET Aspire hosting integration, `KoalaSoft.Aspire.Hosting.Keeper`, that lets an
AppHost resolve secrets stored in Keeper Secrets Manager and expose them as
`ParameterResource` values that other resources can consume (env vars, or
values a consumer composes into its own connection strings), without
requiring the Keeper CLI or an interactive login session.

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
  notation syntax) into real secret values, lazily, no later than the first
  point something actually reads them.
- Support both an explicit builder API (`AddKeeperSecret`) and implicit
  config-scanning (bare `keeper://` strings in `IConfiguration`), mirroring the
  1Password extension's ergonomics.
- Batch all secret lookups triggered within a given run into a single Keeper
  API call, even when resolution is lazy/on-demand.
- Never resolve or leak secret values during `aspire publish` manifest
  generation; fail fast instead if publish would require resolving one.
- Fail fast and loudly on any resolution error (bad token, missing record,
  missing field, or a `keeper://` reference touched during publish) — no
  silent fallback to empty/blank values, and no silent pass-through of an
  unresolved notation string as if it were real data.

## Non-goals

- No custom UI/dashboard integration.
- No support for writing secrets back to Keeper.
- No first-class connection-string composition helpers — v1 exposes resolved
  values as parameters/config values only; consumers compose their own
  connection strings from those values. Built-in composition helpers can be
  added later without breaking changes.
- No automated CI test suite against a real Keeper vault (unit tests use a
  fake client/storage; a separate API-surface smoke test uses the real Aspire
  and Keeper SDK types with a fake resolver; the sample AppHost is a manual
  smoke test against a real vault).

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

The integration has two independent resolution paths that share one resolver
and one batching mechanism, but differ in *when* resolution is triggered —
this split is the direct fix for an ordering bug found in review (see
"Why implicit resolution is lazy, not hook-driven" below).

### Explicit path (`AddKeeperSecret` / `ParameterResource`)

Aspire's `ParameterResource` values are themselves resolved lazily by Aspire
(consuming resources await a parameter's value at their own startup, not at
`Program.cs` execution time), so an `IDistributedApplicationLifecycleHook`
running once before dependent resources start (`BeforeStartEvent` / current
equivalent) is a sound fit for this path:

1. Collects every `KeeperParameterReferenceAnnotation` attached to a
   `ParameterResource` (from explicit `AddKeeperSecret` calls).
2. Extracts the distinct set of record UIDs referenced.
3. Calls `KeeperSecretResolver` once to fetch all of them in a single Keeper
   SDK `GetSecrets` call.
4. Resolves each notation string against the cached result and sets the
   value on its `ParameterResource`, before any consuming resource starts.

### Implicit path (config-scanning) — lazy `IConfigurationProvider`

Bare `keeper://` strings in `IConfiguration` (e.g. `appsettings.json`) are
**not** resolved by the lifecycle hook. Instead, `AddKeeperSecrets(...)`
(see Components) adds a `KeeperConfigurationSource` to the AppHost's
`IConfigurationBuilder`, which must be registered *after* every configuration
source that might contain `keeper://` values (default host builder ordering —
`appsettings.json`, environment variables, user secrets — already satisfies
this as long as `AddKeeperSecrets` is called after `CreateBuilder` and before
any code reads the values in question).

At build time, `KeeperConfigurationSource.Build(IConfigurationBuilder builder)`
needs to inventory `keeper://` values already produced by previously-added
sources. `IConfigurationSource.Build` only receives the builder, not
already-`Load()`-ed provider data, so the inventory step works by
constructing a **temporary, throwaway `ConfigurationRoot`** from
`builder.Sources` as registered so far (i.e. `new
ConfigurationBuilder().Add(...builder.Sources).Build()`), reading its merged
values, then discarding it — the real providers already on `builder` are
untouched and load normally afterward. This means every prior source's
`Load()` runs twice (once for the temporary inventory root, once for the real
one) — a no-op in practice for the sources Aspire AppHosts actually use
(JSON files, environment variables, user secrets), all of which are
side-effect-free to reload. **Constraint:** implicit config-scanning is only
supported when every configuration source registered before
`AddKeeperSecrets()` is side-effect-free on reload; a custom source with
load-time side effects is out of scope for v1 and should use the explicit
`AddKeeperSecret` API instead.

The resulting `KeeperConfigurationProvider`:

1. From that inventory, records the set of keys whose *current* value is a
   bare `keeper://...` string, storing `key → notation` — without resolving
   any of them yet.
2. Does nothing further until `TryGet` is called for one of those keys.

On the **first** `TryGet` call for any recorded key (from anywhere —
`IConfiguration` indexer, `IConfigurationSection.Value`, options binding via
`IOptions<T>`/`IOptionsSnapshot`/`IOptionsMonitor`, or a direct read like
`builder.Configuration["Smtp:Password"]` passed straight into
`WithEnvironment`), the provider:

3. Enters a `SemaphoreSlim`-guarded section and, **inside** the lock, checks
   whether resolution has already completed (double-checked pattern: the
   semaphore only serializes access, the cache-populated check is what
   actually prevents a second resolve). If not yet resolved, it resolves
   **every** recorded `keeper://` notation in one batched
   `KeeperSecretResolver.ResolveAsync` call (not just the one being read) and
   caches the results for the lifetime of the provider. If another thread
   already completed resolution while this thread was waiting on the
   semaphore, it skips straight to using the cache.
4. Returns the resolved value for the requested key; subsequent `TryGet`
   calls for any recorded key take a fast path that checks the cache before
   attempting the lock, and are served from the cache without contention.

Because `TryGet` is a synchronous interface method but `ResolveAsync` is
async, step 3 blocks the calling thread via
`Task.Run(() => resolver.ResolveAsync(...)).GetAwaiter().GetResult()` —
offloading to the thread pool rather than calling `.GetAwaiter().GetResult()`
directly on the current thread, so the call cannot deadlock even if some
future caller runs it under an ambient `SynchronizationContext`. (A plain
.NET generic-host console process, which is what an AppHost is, has no
`SynchronizationContext` by default and wouldn't deadlock either way, but
routing through `Task.Run` removes the dependency on that assumption holding
in every hosting context this code might run under.) This is a one-time
AppHost-startup cost, not a hot path, so the thread-pool hop's overhead is
irrelevant.

**Scope:** the inventory is captured once, at initial configuration build.
Configuration sources with `reloadOnChange` enabled, or values added to
`IConfiguration` after that point, are not re-scanned — a `keeper://` string
introduced after startup is out of scope for v1 and passes through
unresolved. This matches the existing "resolve once at startup" model used by
the explicit path.

#### Why implicit resolution is lazy, not hook-driven

The original design resolved implicit references in the same
`BeforeStartEvent` hook as the explicit path and wrote results back into
`IConfiguration`. That breaks the common Aspire pattern where a value is read
*out* of `IConfiguration` as a plain string synchronously in `Program.cs` —
e.g. `builder.AddProject<Projects.Api>().WithEnvironment("Smtp__Password",
builder.Configuration["Smtp:Password"])` — because that read happens, and the
string is copied, before any lifecycle hook has run. The consuming resource
would receive the literal `"keeper://..."` string. Making resolution a
property of the configuration *provider* itself — so it fires at the moment
of read, wherever and whenever that read happens — removes the ordering
dependency entirely: it doesn't matter whether the read happens in
`Program.cs`, in a later hook, or during options binding at DI-resolution
time, because the provider resolves on first touch regardless.

### Publish-time behavior

- **Explicit path:** at publish time, the hook does not run resolution;
  publishing tools may inspect `KeeperParameterReferenceAnnotation` to know a
  value is secret-sourced, without triggering a Keeper API call or embedding
  the value in the manifest. The generated manifest marks the parameter as a
  secret input (mirroring however Aspire's own built-in secret parameters are
  marked, e.g. `ParameterResource.Secret = true` or the current equivalent),
  so the deploy target is required to supply the value out-of-band rather
  than the manifest silently omitting or embedding it.
- **Implicit path:** `KeeperConfigurationProvider` checks Aspire's publish-mode
  context (`ExecutionContext.IsPublishMode` or the current equivalent) inside
  `TryGet`. If any recorded `keeper://` key is touched while in publish mode,
  it **throws** immediately — naming the key and notation — instead of either
  resolving it (which would embed a live secret in the manifest) or returning
  the unresolved literal (which would silently ship a non-functional value).
  The thrown error tells the user to convert that reference to an explicit
  `AddKeeperSecret` parameter, which publish already handles correctly.
  Implicit config-scanning is therefore dev-run-only by design; publish
  support requires the explicit API.

## Components

### `KeeperSecretsManagerOptions`

Holds:
- `IKeyValueStorage Storage` — defaults to `LocalConfigStorage` pointed at a
  configurable path (default `~/.keeper/aspire-config.json`).
- `string? OneTimeToken` — optional, used only on first run to bootstrap the
  local encrypted config; not needed on subsequent runs once the config file
  exists.

### `AddKeeperSecrets`

```csharp
IDistributedApplicationBuilder AddKeeperSecrets(
    this IDistributedApplicationBuilder builder,
    Action<KeeperSecretsManagerOptions>? configure = null)
```

(Renamed from the earlier `UseKeeper` to match Aspire's `Add*` hosting-method
convention, e.g. `AddRedis`, `AddAzureProvisioning`.)

Registers options, the explicit-path lifecycle hook, and the
`KeeperConfigurationSource` for the implicit path. Called once in the
AppHost's `Program.cs`, **after** every configuration source that may contain
`keeper://` values and before any code reads those values. Fails fast
(throws) if the resulting storage/token state is invalid (e.g., no config
file exists and no one-time token was supplied).

This ordering requirement also covers DI: if an `IOptions<T>` singleton
bound from a `keeper://`-backed section is resolved by the container before
`AddKeeperSecrets()` runs, it captures the literal notation string, since
`IOptions<T>` (unlike `IOptionsSnapshot`/`IOptionsMonitor`) binds once and
caches for the container's lifetime. Registering `AddKeeperSecrets()` early
in `Program.cs`, before any service resolution, avoids this.

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

### `KeeperConfigurationSource` / `KeeperConfigurationProvider`

`IConfigurationSource` / `ConfigurationProvider` pair implementing the lazy
implicit-resolution behavior described in Architecture above: inventories
`keeper://` notations from already-registered sources at build time, defers
resolution to first `TryGet`, resolves the full inventory in one batched call,
caches results, and throws instead of resolving when Aspire is in publish
mode.

### `KeeperSecretResolver`

Thin wrapper around `SecretsManagerClient`:
- `Task<IReadOnlyDictionary<string, string>> ResolveAsync(IEnumerable<string> notations, KeeperSecretsManagerOptions options)`
- Internally: extracts the distinct UID set from the notations, calls
  `SecretsManagerClient.GetSecrets(options, uids)` once, then diffs the
  requested UID set against the UIDs actually present in the response —
  any UID missing from the response throws immediately, naming that UID,
  distinct from a "field not found within a returned record" error (which is
  checked next, per notation, via the SDK's `Notation.GetValue`-equivalent).

## Data Flow Diagram

```
AppHost Program.cs
  ├─ builder.AddKeeperSecrets(...)         → registers options, hook,
  │                                           and KeeperConfigurationSource
  ├─ builder.AddKeeperSecret("db-pw", "keeper://UID/field/password")
  └─ appsettings.json: "Smtp:Password": "keeper://UID2/field/password"

Explicit path — BeforeStartEvent (lifecycle hook)
  ├─ collect KeeperParameterReferenceAnnotations
  ├─ extract distinct UIDs → {UID}
  ├─ KeeperSecretResolver.ResolveAsync(...) → one GetSecrets call
  └─ set ParameterResource("db-pw").Value

Implicit path — first read of a keeper-backed key, whenever it happens
  ├─ e.g. builder.Configuration["Smtp:Password"] (even inline in Program.cs)
  ├─ KeeperConfigurationProvider.TryGet("Smtp:Password", ...)
  ├─ not yet resolved → resolve ALL recorded keeper:// keys in one
  │  GetSecrets call, cache results
  └─ return resolved value for "Smtp:Password"

Publish mode — any implicit key touched
  └─ KeeperConfigurationProvider throws (no resolve, no literal pass-through)

Other resources
  └─ .WithEnvironment("DB_PASSWORD", dbPwParam)  → receives resolved value
```

## Error Handling

- Invalid/missing local config and no one-time token at `AddKeeperSecrets()`
  registration time → throw immediately with a clear message (don't allow the
  app to start with unresolved secrets pending).
- A requested UID absent entirely from a `GetSecrets` response → throw,
  naming that UID, before attempting per-notation field lookups.
- A referenced field not found within a UID that *was* returned → throw,
  naming the specific `keeper://` reference that failed.
- A `keeper://` reference (implicit path) touched while Aspire is in publish
  mode → throw, naming the key and notation, directing the user to the
  explicit `AddKeeperSecret` API.
- No fallback to empty string / null / literal notation string for any
  resolution failure.

## Bootstrap Safety (local config file)

`~/.keeper/aspire-config.json` holds token/key material produced by
exchanging the one-time token, so its creation needs the same care as any
credential file:

- **Atomicity:** bootstrap writes to a temp file in the same directory, then
  atomically renames it over the target path, so a crash or concurrent read
  never observes a partially-written file.
- **Permissions:** the file is created with owner-only permissions (e.g.
  POSIX `0600` / `UnixFileMode.UserRead | UserWrite`) at creation time, before
  any token material is written to it — not applied after the fact.
- **Concurrency:** bootstrap acquires a cross-process lock (a sibling
  `.lock` file or OS file lock) around the "config file missing → exchange
  one-time token → write config" sequence, with a bounded wait. A process
  that fails to acquire the lock in time fails fast with a message indicating
  another process is bootstrapping, rather than racing it. Because Keeper
  one-time tokens are single-use, the lock holder re-checks (after acquiring
  the lock) whether the config file now exists before consuming the token, so
  a second concurrent process observes "already bootstrapped" and proceeds
  normally instead of hitting an opaque "token already used" error from the
  SDK.
- Developers working across multiple Keeper vault identities/projects on one
  machine should override `Storage`'s path per-repo rather than relying on
  the shared default.

## Testing Strategy

- Unit tests (in `KoalaSoft.Aspire.Hosting.Keeper.Tests`) covering:
  - Notation string parsing / UID extraction.
  - Batching logic (N notations across M UIDs → 1 resolver call), including
    the implicit path's "resolve everything recorded on first touch"
    behavior and its double-checked-lock path (concurrent `TryGet` calls
    from multiple threads trigger exactly one `ResolveAsync` call, verified
    via a call-count assertion on the fake resolver).
  - Error propagation for: missing UID (absent from response) vs. missing
    field (present record, absent field), missing token/config, and an
    implicit reference touched during publish mode.
  - Config-scan detection of bare `keeper://` strings and correct precedence
    against earlier-registered configuration sources.
  - Bootstrap concurrency: two simulated concurrent bootstraps against the
    same lock/config path resolve to one winner and one "already
    bootstrapped, proceeding" path, not a crash or corrupt file.
  - All tests use a fake `SecretsManagerClient`/`IKeyValueStorage` — no real
    Keeper vault or network access required.
- API-surface smoke test: a separate test project that references the real
  `Aspire.Hosting` and `Keeper.SecretsManager` packages at their real types
  and method signatures (with a fake resolver injected only at the
  secret-fetching boundary), registering `AddKeeperSecrets`/`AddKeeperSecret`
  against a real `DistributedApplicationBuilder` and asserting it builds
  without runtime signature errors. This exists specifically to catch drift
  against the two SDK surfaces called out below, since the fake-client unit
  tests above cannot.
- The sample AppHost project is a manual integration smoke test run against a
  real (test) Keeper vault; not part of automated CI in v1.

## Open Questions / Risks for Implementation Phase

- Exact current Aspire 8.x API names for lifecycle hooks, publish-mode
  detection (`ExecutionContext.IsPublishMode` or equivalent), and
  `ParameterResource` value-setting / secret-marking need to be verified
  against the actual SDK at implementation time (Aspire's extensibility
  surface has shifted across versions).
- Exact current `Keeper.SecretsManager` NuGet package API (method names for
  batched `GetSecrets` by UID list, notation parsing helper) needs
  verification against the latest SDK version at implementation time.
- Confirm `IDistributedApplicationBuilder.Configuration` exposes an
  `IConfigurationBuilder` with a standard `Sources` list at the point
  `AddKeeperSecrets()` runs, so `KeeperConfigurationSource.Build` can
  construct the temporary inventory root described above. If Aspire's AppHost
  builder wraps configuration differently, the temporary-root technique needs
  adjusting accordingly at implementation time.
