# KoalaSoft.Aspire.Hosting.Keeper

Aspire hosting integration for [Keeper Secrets Manager](https://www.keepersecurity.com/secrets-manager.html).

Resolves `keeper://<record-uid>/field/<field>` references into real secret values during
AppHost startup, either explicitly (a resource you wire into a resource reference) or
implicitly (a `keeper://` value in configuration, resolved on first read).

## Install

```bash
dotnet add package KoalaSoft.Aspire.Hosting.Keeper
```

## Usage

```csharp
var builder = DistributedApplication.CreateBuilder(args);

builder.AddKeeperSecrets(options =>
{
    // Only needed for the first run on a fresh machine/CI runner — a one-time token from
    // Secrets Manager -> Applications -> your app -> Devices -> Add Device.
    options.OneTimeToken = Environment.GetEnvironmentVariable("KEEPER_ONE_TIME_TOKEN");
});

// Explicit: resolved once, before any dependent resource starts.
var dbPassword = builder.AddKeeperSecret("db-password", "keeper://<record-uid>/field/password");

builder.AddContainer("demo", "hello-world")
    .WithEnvironment("DB_PASSWORD", dbPassword);

builder.Build().Run();
```

Configuration values can also reference `keeper://` URIs directly (e.g. in
`appsettings.json`); those resolve lazily the first time they're read.

See `samples/AppHost` for a runnable end-to-end example, including the implicit
configuration path.

## TypeScript AppHost (polyglot)

`AddKeeperSecrets` and `AddKeeperSecret` are annotated `[AspireExport]`, so a
TypeScript AppHost (Aspire 13.2+) can call them once you add this package to
`aspire.config.json`:

```json
{
  "packages": {
    "KoalaSoft.Aspire.Hosting.Keeper": "%ASPIRE_VERSION%"
  }
}
```

```typescript
import { createBuilder } from './.aspire/modules/aspire.mjs';

const builder = await createBuilder();

await builder.addKeeperSecrets({ oneTimeToken: process.env.KEEPER_ONE_TIME_TOKEN });
const dbPassword = await builder.addKeeperSecret("db-password", "keeper://<record-uid>/field/password");

const demo = await builder.addContainer("demo", { image: "hello-world" });
await demo.withEnvironment("DB_PASSWORD", dbPassword);

await builder.build().run();
```

The **implicit** `keeper://`-in-configuration path only resolves values read through
the .NET orchestration host's own `IConfiguration` (its environment variables, or an
`appsettings.json` when the AppHost is a `.csproj`-based project). A TypeScript
AppHost is configured entirely through `aspire.config.json`, which isn't wired into
that `IConfiguration`, so `keeper://` values embedded there won't resolve implicitly.
From TypeScript, use the explicit `addKeeperSecret(...)` call above for every secret.

## License

MIT
