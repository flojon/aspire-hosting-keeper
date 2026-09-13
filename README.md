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

## License

MIT
