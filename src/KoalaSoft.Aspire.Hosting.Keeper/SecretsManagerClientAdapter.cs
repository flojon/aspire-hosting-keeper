using SecretsManager;

namespace KoalaSoft.Aspire.Hosting.Keeper;

internal sealed class SecretsManagerClientAdapter : IKeeperSecretsClient
{
    public static readonly SecretsManagerClientAdapter Instance = new();

    public Task<KeeperSecrets> GetSecretsAsync(SecretsManagerOptions options, string[] uids, CancellationToken cancellationToken)
        => SecretsManagerClient.GetSecrets(options, uids);
}
