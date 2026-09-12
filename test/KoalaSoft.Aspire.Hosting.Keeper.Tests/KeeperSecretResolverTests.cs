using KoalaSoft.Aspire.Hosting.Keeper;
using KoalaSoft.Aspire.Hosting.Keeper.Tests.Fakes;
using Xunit;

namespace KoalaSoft.Aspire.Hosting.Keeper.Tests;

public class KeeperSecretResolverTests
{
    [Theory]
    [InlineData("keeper://ABC123/field/password", "ABC123")]
    [InlineData("keeper://ABC123/field/password[0]", "ABC123")]
    [InlineData("keeper://ABC123/custom_field/My Label", "ABC123")]
    public void ExtractUid_ParsesUidOutOfNotation(string notation, string expectedUid)
    {
        Assert.Equal(expectedUid, KeeperSecretResolver.ExtractUid(notation));
    }

    [Fact]
    public void ExtractUid_ThrowsOnNonKeeperNotation()
    {
        Assert.Throws<FormatException>(() => KeeperSecretResolver.ExtractUid("not-a-keeper-uri"));
    }

    [Fact]
    public async Task ResolveAsync_BatchesDistinctUidsIntoOneCall()
    {
        var record = FakeKeeperSecretsClient.MakeRecord("UID1", ("password", "secret-value"), ("login", "user@example.com"));
        var client = new FakeKeeperSecretsClient(record);
        var resolver = new KeeperSecretResolver(client);

        var results = await resolver.ResolveAsync(
            new[] { "keeper://UID1/field/password", "keeper://UID1/field/login" },
            new KeeperSecretsManagerOptions());

        Assert.Equal(1, client.CallCount);
        Assert.Single(client.RequestedUidBatches);
        Assert.Equal(new[] { "UID1" }, client.RequestedUidBatches[0]);
        Assert.Equal("secret-value", results["keeper://UID1/field/password"]);
        Assert.Equal("user@example.com", results["keeper://UID1/field/login"]);
    }

    [Fact]
    public async Task ResolveAsync_TwoUidsAcrossThreeNotations_MakesOneCallWithBothUids()
    {
        var recordA = FakeKeeperSecretsClient.MakeRecord("UIDA", ("password", "pw-a"));
        var recordB = FakeKeeperSecretsClient.MakeRecord("UIDB", ("password", "pw-b"));
        var client = new FakeKeeperSecretsClient(recordA, recordB);
        var resolver = new KeeperSecretResolver(client);

        var results = await resolver.ResolveAsync(
            new[] { "keeper://UIDA/field/password", "keeper://UIDB/field/password", "keeper://UIDA/field/password" },
            new KeeperSecretsManagerOptions());

        Assert.Equal(1, client.CallCount);
        Assert.Equal(new[] { "UIDA", "UIDB" }, client.RequestedUidBatches[0].OrderBy(x => x));
        Assert.Equal(2, results.Count);
    }

    [Fact]
    public async Task ResolveAsync_MissingUidInResponse_ThrowsNamingTheUid()
    {
        var client = new FakeKeeperSecretsClient(); // no records at all
        var resolver = new KeeperSecretResolver(client);

        var ex = await Assert.ThrowsAsync<KeeperResolutionException>(() =>
            resolver.ResolveAsync(new[] { "keeper://MISSING/field/password" }, new KeeperSecretsManagerOptions()));

        Assert.Contains("MISSING", ex.Message);
    }

    [Fact]
    public async Task ResolveAsync_MissingFieldOnPresentRecord_ThrowsNamingTheNotation()
    {
        var record = FakeKeeperSecretsClient.MakeRecord("UID1", ("login", "user@example.com")); // no "password" field
        var client = new FakeKeeperSecretsClient(record);
        var resolver = new KeeperSecretResolver(client);

        var ex = await Assert.ThrowsAsync<KeeperResolutionException>(() =>
            resolver.ResolveAsync(new[] { "keeper://UID1/field/password" }, new KeeperSecretsManagerOptions()));

        Assert.Contains("keeper://UID1/field/password", ex.Message);
    }
}
