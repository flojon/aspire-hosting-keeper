using SecretsManager;

namespace KoalaSoft.Aspire.Hosting.Keeper.Tests.Fakes;

internal sealed class FakeKeeperTokenExchanger : IKeeperTokenExchanger
{
    private int _callCount;
    public int CallCount => _callCount;

    public void InitializeStorage(IKeyValueStorage storage, string oneTimeToken, string? hostName)
    {
        Interlocked.Increment(ref _callCount);
        storage.SaveString("clientId", "fake-client-id-for-" + oneTimeToken);
    }
}
