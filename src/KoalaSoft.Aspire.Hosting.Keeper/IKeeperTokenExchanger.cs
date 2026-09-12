using SecretsManager;

namespace KoalaSoft.Aspire.Hosting.Keeper;

/// <summary>
/// Seam over <see cref="SecretsManagerClient.InitializeStorage"/>, which is a static SDK method
/// that performs real network I/O and therefore isn't substitutable directly in unit tests.
/// </summary>
internal interface IKeeperTokenExchanger
{
    void InitializeStorage(IKeyValueStorage storage, string oneTimeToken, string? hostName);
}
