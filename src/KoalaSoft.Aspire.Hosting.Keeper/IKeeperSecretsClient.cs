using SecretsManager;

namespace KoalaSoft.Aspire.Hosting.Keeper;

/// <summary>
/// Seam over <see cref="SecretsManagerClient.GetSecrets"/>, which is a static SDK method and
/// therefore not substitutable directly in unit tests.
/// </summary>
internal interface IKeeperSecretsClient
{
    Task<KeeperSecrets> GetSecretsAsync(SecretsManagerOptions options, string[] uids, CancellationToken cancellationToken);
}
