# Sample AppHost

Manual integration smoke test against a real Keeper vault — not part of automated CI.

1. In the Keeper vault UI, create a Secrets Manager application and note a record UID with a
   `password` field.
2. Replace `REPLACE_WITH_REAL_UID` in `Program.cs` and `appsettings.json` with that UID.
3. Generate a one-time token for the application (Applications -> your app -> Devices -> Add Device).
4. First run: `KEEPER_ONE_TIME_TOKEN=<token> dotnet run --project samples/AppHost`
5. Subsequent runs (device already bootstrapped at `~/.keeper/aspire-config.json`):
   `dotnet run --project samples/AppHost`
6. Verify: the `demo` container starts with `DB_PASSWORD`/`SMTP_PASSWORD` env vars set to the
   real secret values (check via `docker inspect` or the Aspire dashboard's resource details).
7. Verify publish-mode safety: `dotnet run --project samples/AppHost -- --publisher manifest`
   must fail with a `KeeperResolutionException` naming `Smtp:Password`, since that key is only
   wired through the implicit path in this sample.
