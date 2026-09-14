using KoalaSoft.Aspire.Hosting.Keeper.Tests.Fakes;
using SecretsManager;
using Xunit;

namespace KoalaSoft.Aspire.Hosting.Keeper.Tests;

public class KeeperConfigBootstrapperTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("keeper-bootstrap-tests-").FullName;

    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    private string ConfigPath => Path.Combine(_tempDir, "aspire-config.json");

    [Fact]
    public void EnsureBootstrapped_NoConfigAndNoToken_Throws()
    {
        var options = new KeeperSecretsManagerOptions { ConfigPath = ConfigPath };

        var ex = Assert.Throws<KeeperResolutionException>(() => KeeperConfigBootstrapper.EnsureBootstrapped(options));

        Assert.Contains(ConfigPath, ex.Message);
    }

    [Fact]
    public void EnsureBootstrapped_WithToken_CreatesConfigAtomicallyWithOwnerOnlyPermissions()
    {
        var options = new KeeperSecretsManagerOptions { ConfigPath = ConfigPath, OneTimeToken = "fake-token" };
        var exchanger = new FakeKeeperTokenExchanger();

        KeeperConfigBootstrapper.EnsureBootstrapped(options, exchanger);

        Assert.True(File.Exists(ConfigPath));
        Assert.Equal(1, exchanger.CallCount);
        Assert.False(File.Exists(ConfigPath + ".tmp") || Directory.GetFiles(_tempDir, "*.tmp-*").Length > 0);
        if (!OperatingSystem.IsWindows())
        {
            var mode = File.GetUnixFileMode(ConfigPath);
            Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, mode);
        }
    }

    [Fact]
    public void EnsureBootstrapped_ConfigAlreadyExists_SkipsExchangeEvenWithTokenSupplied()
    {
        File.WriteAllText(ConfigPath, "{\"already\":\"bootstrapped\"}");
        var options = new KeeperSecretsManagerOptions { ConfigPath = ConfigPath, OneTimeToken = "fake-token" };
        var exchanger = new FakeKeeperTokenExchanger();

        KeeperConfigBootstrapper.EnsureBootstrapped(options, exchanger);

        Assert.Equal(0, exchanger.CallCount);
    }

    [Fact]
    public void EnsureBootstrapped_StorageSetDirectly_ExchangesViaCustomStorageAndDoesNotOverwriteIt()
    {
        var customPath = Path.Combine(_tempDir, "custom-storage.json");
        var customStorage = new LocalConfigStorage(customPath);
        var options = new KeeperSecretsManagerOptions
        {
            Storage = customStorage,
            OneTimeToken = "fake-token",
        };
        var exchanger = new FakeKeeperTokenExchanger();

        KeeperConfigBootstrapper.EnsureBootstrapped(options, exchanger);

        Assert.Equal(1, exchanger.CallCount);
        Assert.Same(customStorage, options.Storage);
        Assert.False(File.Exists(ConfigPath));
    }

    [Fact]
    public void EnsureBootstrapped_ConcurrentCallers_ExchangeExactlyOnce()
    {
        var options = new KeeperSecretsManagerOptions { ConfigPath = ConfigPath, OneTimeToken = "fake-token" };
        var exchanger = new FakeKeeperTokenExchanger();

        // Capping real concurrency (rather than letting all 8 threads run free, as
        // Parallel.For would) forces at least one caller's once-only check to run strictly
        // after the first bootstrap completes: with only maxConcurrentCallers slots, most of
        // the 8 threads must wait for a slot to free, and the first slot to free is always the
        // one that finished the real bootstrap (a caller blocked on the file lock cannot finish
        // before the lock holder releases it). That reproduces exactly the ordering the #2 bug
        // needed to manifest, deterministically and without depending on core count or sleeps.
        const int callerCount = 8;
        const int maxConcurrentCallers = 2;
        using var allCallersReady = new Barrier(callerCount);
        using var gate = new SemaphoreSlim(maxConcurrentCallers, maxConcurrentCallers);

        var threads = new Thread[callerCount];
        for (var i = 0; i < callerCount; i++)
        {
            threads[i] = new Thread(() =>
            {
                allCallersReady.SignalAndWait();
                gate.Wait();
                try
                {
                    KeeperConfigBootstrapper.EnsureBootstrapped(options, exchanger);
                }
                finally
                {
                    gate.Release();
                }
            });
            threads[i].Start();
        }

        foreach (var thread in threads)
        {
            thread.Join();
        }

        Assert.Equal(1, exchanger.CallCount);
        Assert.True(File.Exists(ConfigPath));
    }
}
