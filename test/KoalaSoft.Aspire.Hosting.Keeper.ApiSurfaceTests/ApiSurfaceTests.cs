using Aspire.Hosting;
using KoalaSoft.Aspire.Hosting.Keeper;
using Xunit;

namespace KoalaSoft.Aspire.Hosting.Keeper.ApiSurfaceTests;

/// <summary>
/// Builds a real DistributedApplicationBuilder against the real Aspire.Hosting and
/// Keeper.SecretsManager packages, to catch drift against those two SDK surfaces that the
/// fake-client unit tests (which only touch internal seam interfaces) cannot catch.
/// </summary>
public class ApiSurfaceTests : IDisposable
{
    private readonly string _tempDir = Directory.CreateTempSubdirectory("keeper-api-surface-tests-").FullName;
    public void Dispose() => Directory.Delete(_tempDir, recursive: true);

    [Fact]
    public void AddKeeperSecretsAndAddKeeperSecret_BuildAgainstRealAspireAndKeeperTypes()
    {
        var configPath = Path.Combine(_tempDir, "config.json");
        File.WriteAllText(configPath, "{\"bootstrapped\":true}");

        var builder = DistributedApplication.CreateBuilder(Array.Empty<string>());

        builder.AddKeeperSecrets(options => options.ConfigPath = configPath);
        var parameter = builder.AddKeeperSecret("db-password", "keeper://UID/field/password");

        using var app = builder.Build();

        Assert.Equal("db-password", parameter.Resource.Name);
    }

    [Fact]
    public void AddKeeperSecrets_InPublishMode_SkipsBootstrapEvenWithNoConfigAndNoToken()
    {
        // "--operation publish" is how DistributedApplicationBuilder itself detects publish mode
        // (Aspire.Hosting's own AddCommandLine switch mapping), so this is real publish mode, not a fake.
        var builder = DistributedApplication.CreateBuilder(new[] { "--operation", "publish" });
        Assert.True(builder.ExecutionContext.IsPublishMode);

        var missingConfigPath = Path.Combine(_tempDir, "does-not-exist.json");

        // No exception: bootstrap must be skipped entirely in publish mode, not merely tolerant
        // of a missing config file (there is neither a config file nor a OneTimeToken here).
        builder.AddKeeperSecrets(options => options.ConfigPath = missingConfigPath);

        Assert.False(File.Exists(missingConfigPath));
    }
}
