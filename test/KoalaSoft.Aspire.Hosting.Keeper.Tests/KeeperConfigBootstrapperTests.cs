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

        Parallel.For(0, 8, _ => KeeperConfigBootstrapper.EnsureBootstrapped(options, exchanger));

        Assert.Equal(1, exchanger.CallCount);
        Assert.True(File.Exists(ConfigPath));
    }
}
