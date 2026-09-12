using Aspire.Hosting.ApplicationModel;
using KoalaSoft.Aspire.Hosting.Keeper.Tests.Fakes;
using Xunit;

namespace KoalaSoft.Aspire.Hosting.Keeper.Tests;

public class KeeperResolutionLifecycleHookTests
{
    [Fact]
    public async Task BeforeStartAsync_ResolvesAllAnnotatedParameters_InOneBatchedCall()
    {
        var record = FakeKeeperSecretsClient.MakeRecord("UID1", ("password", "db-secret"));
        var client = new FakeKeeperSecretsClient(record);
        var resolver = new KeeperSecretResolver(client);
        var hook = new KeeperResolutionLifecycleHook(resolver, new KeeperSecretsManagerOptions());

        var param1 = new ParameterResource("db-pw", _ => hook.ResolvedValues["keeper://UID1/field/password"], secret: true);
        param1.Annotations.Add(new KeeperParameterReferenceAnnotation("keeper://UID1/field/password"));
        var param2 = new ParameterResource("db-pw-2", _ => hook.ResolvedValues["keeper://UID1/field/password"], secret: true);
        param2.Annotations.Add(new KeeperParameterReferenceAnnotation("keeper://UID1/field/password"));
        var unrelated = new ParameterResource("not-keeper", _ => "plain", secret: false);

        var model = new DistributedApplicationModel(new ResourceCollectionStub(param1, param2, unrelated));

        await hook.BeforeStartAsync(model, CancellationToken.None);

        Assert.Equal(1, client.CallCount);
        Assert.Equal("db-secret", param1.Value);
        Assert.Equal("db-secret", param2.Value);
    }

    [Fact]
    public async Task BeforeStartAsync_NoAnnotatedParameters_DoesNotCallResolver()
    {
        var client = new FakeKeeperSecretsClient();
        var resolver = new KeeperSecretResolver(client);
        var hook = new KeeperResolutionLifecycleHook(resolver, new KeeperSecretsManagerOptions());
        var unrelated = new ParameterResource("not-keeper", _ => "plain", secret: false);
        var model = new DistributedApplicationModel(new ResourceCollectionStub(unrelated));

        await hook.BeforeStartAsync(model, CancellationToken.None);

        Assert.Equal(0, client.CallCount);
    }
}
