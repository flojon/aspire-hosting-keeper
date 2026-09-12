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
}
