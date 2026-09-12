using Aspire.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Xunit;

namespace KoalaSoft.Aspire.Hosting.Keeper.Tests;

public class AddKeeperSecretsTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("add-keeper-secrets-tests-").FullName;
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void AddKeeperSecrets_RegistersLifecycleHook()
    {
        var builder = DistributedApplication.CreateBuilder(Array.Empty<string>());
        var configPath = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(configPath, "{\"bootstrapped\":true}"); // pre-bootstrapped: no token needed

        builder.AddKeeperSecrets(o => o.ConfigPath = configPath);

        using var provider = builder.Services.BuildServiceProvider();
        // global:: required: this file's own namespace (KoalaSoft.Aspire.Hosting.Keeper.Tests) nests
        // inside "KoalaSoft.Aspire.Hosting", so an unqualified "Aspire.Hosting.Lifecycle" reference
        // resolves relative to that enclosing namespace instead of the top-level Aspire.Hosting package.
        var hooks = provider.GetServices<global::Aspire.Hosting.Lifecycle.IDistributedApplicationLifecycleHook>();
        Assert.Contains(hooks, h => h is KeeperResolutionLifecycleHook);
    }

    [Fact]
    public void AddKeeperSecrets_ThenAddKeeperSecret_ShareTheSameHookInstance()
    {
        var builder = DistributedApplication.CreateBuilder(Array.Empty<string>());
        var configPath = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(configPath, "{\"bootstrapped\":true}");

        builder.AddKeeperSecrets(o => o.ConfigPath = configPath);
        builder.AddKeeperSecret("db-pw", "keeper://UID/field/password");

        Assert.True(KeeperSecretsManagerBuilderExtensions.HooksByBuilder.TryGetValue(builder, out _));
    }

    [Fact]
    public void AddKeeperSecrets_NoConfigAndNoToken_ThrowsImmediately()
    {
        var builder = DistributedApplication.CreateBuilder(Array.Empty<string>());
        var configPath = Path.Combine(_tempDir, "missing-config.json");

        Assert.Throws<KeeperResolutionException>(() => builder.AddKeeperSecrets(o => o.ConfigPath = configPath));
    }
}
