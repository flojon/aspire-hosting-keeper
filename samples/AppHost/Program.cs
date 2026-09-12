using KoalaSoft.Aspire.Hosting.Keeper;

var builder = DistributedApplication.CreateBuilder(args);

// Registers Keeper support. Must come after configuration is set up (default host builder
// ordering already covers appsettings.json/env vars/user secrets) and before anything reads
// keeper:// values below.
builder.AddKeeperSecrets(options =>
{
    // First run against a fresh machine: pass a one-time token generated in the Keeper vault UI
    // (Secrets Manager -> Applications -> your app -> Devices -> Add Device), e.g. via:
    //   KEEPER_ONE_TIME_TOKEN=<token> dotnet run --project samples/AppHost
    options.OneTimeToken = Environment.GetEnvironmentVariable("KEEPER_ONE_TIME_TOKEN");
});

// Explicit path: resolved once, before any dependent resource starts.
var dbPassword = builder.AddKeeperSecret("db-password", "keeper://REPLACE_WITH_REAL_UID/field/password");

// Implicit path: builder.Configuration["Smtp:Password"] resolves lazily on first read, even
// though this read happens synchronously right here in Program.cs.
var smtpPassword = builder.Configuration["Smtp:Password"];

builder.AddContainer("demo", "hello-world")
    .WithEnvironment("DB_PASSWORD", dbPassword)
    .WithEnvironment("SMTP_PASSWORD", smtpPassword ?? throw new InvalidOperationException("Smtp:Password not configured"));

builder.Build().Run();
