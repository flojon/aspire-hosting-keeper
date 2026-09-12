using KoalaSoft.Aspire.Hosting.Keeper.Tests.Fakes;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace KoalaSoft.Aspire.Hosting.Keeper.Tests;

public class KeeperConfigurationProviderTests
{
    private static IConfigurationBuilder BuilderWithInMemory(params (string key, string value)[] values)
    {
        var builder = new ConfigurationBuilder();
        builder.AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.key, v.value)));
        return builder;
    }

    [Fact]
    public void Build_InventoriesKeeperStringsFromPriorSources_WithoutResolving()
    {
        var client = new FakeKeeperSecretsClient(); // would throw if ever called
        var resolver = new KeeperSecretResolver(client);
        var builder = BuilderWithInMemory(
            ("Smtp:Password", "keeper://UID/field/password"),
            ("Smtp:Host", "smtp.example.com"));
        var source = new KeeperConfigurationSource(resolver, new KeeperSecretsManagerOptions(), () => false);
        builder.Add(source);

        var root = builder.Build(); // Build() alone must not resolve anything

        Assert.Equal(0, client.CallCount);
        Assert.Equal("smtp.example.com", root["Smtp:Host"]);
    }

    [Fact]
    public void TryGet_FirstTouch_ResolvesAllRecordedKeysInOneBatchedCall()
    {
        var record = FakeKeeperSecretsClient.MakeRecord("UID", ("password", "smtp-secret"), ("login", "smtp-user"));
        var client = new FakeKeeperSecretsClient(record);
        var resolver = new KeeperSecretResolver(client);
        var builder = BuilderWithInMemory(
            ("Smtp:Password", "keeper://UID/field/password"),
            ("Smtp:User", "keeper://UID/field/login"),
            ("Smtp:Host", "smtp.example.com"));
        var source = new KeeperConfigurationSource(resolver, new KeeperSecretsManagerOptions(), () => false);
        builder.Add(source);
        var root = builder.Build();

        var password = root["Smtp:Password"];
        var user = root["Smtp:User"];

        Assert.Equal("smtp-secret", password);
        Assert.Equal("smtp-user", user);
        Assert.Equal(1, client.CallCount); // both keys resolved by the first touch
    }

    [Fact]
    public void TryGet_ConcurrentFirstTouches_ResolveExactlyOnce()
    {
        var record = FakeKeeperSecretsClient.MakeRecord("UID", ("password", "smtp-secret"));
        var client = new FakeKeeperSecretsClient(record);
        var resolver = new KeeperSecretResolver(client);
        var builder = BuilderWithInMemory(("Smtp:Password", "keeper://UID/field/password"));
        var source = new KeeperConfigurationSource(resolver, new KeeperSecretsManagerOptions(), () => false);
        builder.Add(source);
        var root = builder.Build();

        Parallel.For(0, 16, _ => { var value = root["Smtp:Password"]; });

        Assert.Equal(1, client.CallCount);
    }

    [Fact]
    public void TryGet_DuringPublishMode_Throws()
    {
        var client = new FakeKeeperSecretsClient();
        var resolver = new KeeperSecretResolver(client);
        var builder = BuilderWithInMemory(("Smtp:Password", "keeper://UID/field/password"));
        var source = new KeeperConfigurationSource(resolver, new KeeperSecretsManagerOptions(), () => true);
        builder.Add(source);
        var root = builder.Build();

        var ex = Assert.Throws<KeeperResolutionException>(() => _ = root["Smtp:Password"]);

        Assert.Contains("Smtp:Password", ex.Message);
        Assert.Contains("keeper://UID/field/password", ex.Message);
        Assert.Equal(0, client.CallCount);
    }

    [Fact]
    public void TryGet_ForKeyNotRecordedAsKeeper_FallsThroughNormally()
    {
        var client = new FakeKeeperSecretsClient();
        var resolver = new KeeperSecretResolver(client);
        var builder = BuilderWithInMemory(
            ("Smtp:Password", "keeper://UID/field/password"),
            ("Smtp:Host", "smtp.example.com"));
        var source = new KeeperConfigurationSource(resolver, new KeeperSecretsManagerOptions(), () => false);
        builder.Add(source);
        var root = builder.Build();

        Assert.Equal("smtp.example.com", root["Smtp:Host"]);
        Assert.Equal(0, client.CallCount);
    }
}
