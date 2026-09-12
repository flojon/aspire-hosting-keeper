using KoalaSoft.Aspire.Hosting.Keeper;
using SecretsManager;
using Xunit;

namespace KoalaSoft.Aspire.Hosting.Keeper.Tests;

public class KeeperSecretsManagerOptionsTests
{
    [Fact]
    public void DefaultStorage_PointsAtDefaultConfigPath()
    {
        var options = new KeeperSecretsManagerOptions();

        Assert.Equal(KeeperSecretsManagerOptions.DefaultConfigPath, options.ConfigPath);
        Assert.IsType<LocalConfigStorage>(options.Storage);
    }

    [Fact]
    public void SettingConfigPath_RebindsStorageToLocalConfigStorage()
    {
        var options = new KeeperSecretsManagerOptions();
        var customPath = "/tmp/some-other-keeper-config.json";

        options.ConfigPath = customPath;

        Assert.Equal(customPath, options.ConfigPath);
        Assert.IsType<LocalConfigStorage>(options.Storage);
    }

    [Fact]
    public void SettingStorageDirectly_DoesNotChangeConfigPath()
    {
        var options = new KeeperSecretsManagerOptions();
        var originalConfigPath = options.ConfigPath;
        var fake = new FakeKeyValueStorage();

        options.Storage = fake;

        Assert.Same(fake, options.Storage);
        Assert.Equal(originalConfigPath, options.ConfigPath);
    }
}

internal sealed class FakeKeyValueStorage : IKeyValueStorage
{
    private readonly Dictionary<string, string> _strings = new();
    private readonly Dictionary<string, byte[]> _bytes = new();
    public string? GetString(string key) => _strings.TryGetValue(key, out var v) ? v : null;
    public byte[]? GetBytes(string key) => _bytes.TryGetValue(key, out var v) ? v : null;
    public void SaveString(string key, string value) => _strings[key] = value;
    public void SaveBytes(string key, byte[] value) => _bytes[key] = value;
    public void Delete(string key) { _strings.Remove(key); _bytes.Remove(key); }
}
