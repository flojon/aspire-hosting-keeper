using SecretsManager;

namespace KoalaSoft.Aspire.Hosting.Keeper;

/// <summary>
/// Bootstraps the local Keeper device config file, if <see cref="KeeperSecretsManagerOptions.Storage"/>
/// is the default <see cref="LocalConfigStorage"/> pointed at <see cref="KeeperSecretsManagerOptions.ConfigPath"/>.
/// Applies the same care as any credential file: atomic create-then-rename, owner-only permissions
/// applied before any token material is written, and a cross-process lock around the
/// check-then-exchange-then-write sequence (re-checked after the lock is held, since Keeper
/// one-time tokens are single-use).
/// </summary>
internal static class KeeperConfigBootstrapper
{
    private static readonly TimeSpan LockTimeout = TimeSpan.FromSeconds(30);

    public static void EnsureBootstrapped(KeeperSecretsManagerOptions options, IKeeperTokenExchanger? exchanger = null)
    {
        exchanger ??= SecretsManagerClientTokenExchanger.Instance;

        if (!options.StorageIsManagedByConfigPath)
        {
            // Storage was assigned directly by the caller (even if it happens to be a
            // LocalConfigStorage) rather than produced by the ConfigPath setter: it is not "the
            // managed default path", so bootstrap-file atomicity/permissions/locking don't apply,
            // and ConfigPath must not be consulted. Only exchange the token if one was supplied;
            // otherwise trust the caller's storage already holds valid device credentials.
            if (options.OneTimeToken is not null)
            {
                exchanger.InitializeStorage(options.Storage, options.OneTimeToken, hostName: null);
            }
            return;
        }

        var configPath = options.ConfigPath;
        if (File.Exists(configPath))
        {
            return; // already bootstrapped
        }

        if (options.OneTimeToken is null)
        {
            throw new KeeperResolutionException(
                $"No Keeper local config found at '{configPath}' and no OneTimeToken was supplied. " +
                "Provide a one-time token (via KeeperSecretsManagerOptions.OneTimeToken) to bootstrap it, " +
                "or point ConfigPath/Storage at an existing config.");
        }

        var directory = Path.GetDirectoryName(configPath)!;
        Directory.CreateDirectory(directory);
        var lockPath = configPath + ".lock";

        using var lockHandle = AcquireLock(lockPath, LockTimeout, configPath);

        // Re-check now that we hold the lock: another process may have finished bootstrapping
        // while we waited, and Keeper one-time tokens are single-use.
        if (File.Exists(configPath))
        {
            return;
        }

        BootstrapViaTempFileAndRename(options, configPath, exchanger);
    }

    private static void BootstrapViaTempFileAndRename(
        KeeperSecretsManagerOptions options, string configPath, IKeeperTokenExchanger exchanger)
    {
        var tempPath = configPath + ".tmp-" + Guid.NewGuid().ToString("N");
        try
        {
            CreateEmptyFileWithOwnerOnlyPermissions(tempPath);

            var tempStorage = new LocalConfigStorage(tempPath);
            exchanger.InitializeStorage(tempStorage, options.OneTimeToken!, hostName: null);

            // Defensive: re-assert owner-only permissions after the SDK write, in case its
            // internal write path recreated the file rather than writing in place.
            SetOwnerOnlyPermissionsIfSupported(tempPath);

            File.Move(tempPath, configPath);
            options.SetManagedStorage(new LocalConfigStorage(configPath));
        }
        finally
        {
            if (File.Exists(tempPath))
            {
                File.Delete(tempPath);
            }
        }
    }

    private static void CreateEmptyFileWithOwnerOnlyPermissions(string path)
    {
        // "{}" (not empty content): LocalConfigStorage parses the file as JSON as soon as it
        // exists, and Utf8JsonReader rejects a zero-length payload as invalid JSON.
        var emptyConfig = "{}"u8.ToArray();

        if (OperatingSystem.IsWindows())
        {
            File.WriteAllBytes(path, emptyConfig);
            return;
        }

        var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        using var handle = File.Open(path, new FileStreamOptions
        {
            Mode = FileMode.CreateNew,
            Access = FileAccess.Write,
            UnixCreateMode = mode,
        });
        handle.Write(emptyConfig);
    }

    private static void SetOwnerOnlyPermissionsIfSupported(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }

    private static FileStream AcquireLock(string lockPath, TimeSpan timeout, string configPath)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true)
        {
            try
            {
                return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException) when (DateTime.UtcNow < deadline)
            {
                Thread.Sleep(100);
            }
            catch (IOException ex)
            {
                throw new KeeperResolutionException(
                    $"Timed out after {timeout.TotalSeconds}s waiting for another process to finish " +
                    $"bootstrapping the Keeper config at '{configPath}'. If no other process is running, " +
                    $"delete the stale lock file at '{lockPath}'.", ex);
            }
        }
    }
}
