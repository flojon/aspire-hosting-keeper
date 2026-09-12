using SecretsManager;

namespace KoalaSoft.Aspire.Hosting.Keeper;

internal sealed class SecretsManagerClientTokenExchanger : IKeeperTokenExchanger
{
    public static readonly SecretsManagerClientTokenExchanger Instance = new();

    public void InitializeStorage(IKeyValueStorage storage, string oneTimeToken, string? hostName)
        => SecretsManagerClient.InitializeStorage(storage, oneTimeToken, hostName);
}
