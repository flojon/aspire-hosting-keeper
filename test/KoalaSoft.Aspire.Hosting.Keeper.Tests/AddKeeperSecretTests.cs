using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Xunit;

namespace KoalaSoft.Aspire.Hosting.Keeper.Tests;

public class AddKeeperSecretTests
{
    [Fact]
    public void AddKeeperSecret_CreatesSecretParameterResource_WithReferenceAnnotation()
    {
        var builder = DistributedApplication.CreateBuilder(Array.Empty<string>());

        var resourceBuilder = builder.AddKeeperSecret("db-pw", "keeper://UID/field/password");

        Assert.Equal("db-pw", resourceBuilder.Resource.Name);
        Assert.True(resourceBuilder.Resource.Secret);
        Assert.True(resourceBuilder.Resource.TryGetAnnotationsOfType<KeeperParameterReferenceAnnotation>(out var annotations));
        Assert.Equal("keeper://UID/field/password", Assert.Single(annotations).Notation);
    }

    [Fact]
    public void AddKeeperSecret_ValueCallback_ThrowsIfNoHookWasRegistered()
    {
        // AddKeeperSecrets() (Task 7) is what registers the hook AddKeeperSecret's callback reads
        // from; calling AddKeeperSecret without it first must fail clearly, not with a raw
        // KeyNotFoundException from an internal dictionary lookup.
        var builder = DistributedApplication.CreateBuilder(Array.Empty<string>());
        var resourceBuilder = builder.AddKeeperSecret("db-pw", "keeper://UID/field/password");

        Assert.Throws<InvalidOperationException>(() => resourceBuilder.Resource.Value);
    }
}
