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

        // Capping real concurrency (rather than letting all 8 run free, as Parallel.For
        // would) forces a happens-before edge the bug needs to survive: the winner's
        // gate.Release() is ordered, by that thread's own program order, strictly after its
        // state mutation, and SemaphoreSlim's release/wait pair is a full memory fence — so
        // any caller newly admitted through that release is guaranteed to observe the
        // winner's post-exchange state. With only maxConcurrentCallers slots and 8 callers,
        // at least one caller is always admitted this way, deterministically and without
        // depending on core count or sleeps.
        const int callerCount = 8;
        const int maxConcurrentCallers = 2;
        using var allCallersReady = new Barrier(callerCount);
        using var gate = new SemaphoreSlim(maxConcurrentCallers, maxConcurrentCallers);

        // LongRunning gets each caller a dedicated thread rather than a thread-pool slot —
        // with 8 callers blocking on Barrier/SemaphoreSlim, pool injection throttling could
        // otherwise starve this on a low-core machine. Using Task (not raw Thread) still
        // means an unexpected exception surfaces as a test failure via the awaited Task,
        // rather than crashing the process on an unobserved thread.
        var callers = new Task[callerCount];
        for (var i = 0; i < callerCount; i++)
        {
            callers[i] = Task.Factory.StartNew(() =>
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
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        Task.WaitAll(callers);

        Assert.Equal(1, exchanger.CallCount);
        Assert.True(File.Exists(ConfigPath));
    }
}
