using SecretsManager;

namespace KoalaSoft.Aspire.Hosting.Keeper;

/// <summary>
/// Resolves a set of <c>keeper://</c> notations by extracting the distinct record UIDs they
/// reference, fetching all of them in a single Keeper API call, and resolving each notation's
/// field against the batched response.
/// </summary>
internal sealed class KeeperSecretResolver
{
    private readonly IKeeperSecretsClient _client;

    public KeeperSecretResolver(IKeeperSecretsClient client)
    {
        _client = client;
    }

    public async Task<IReadOnlyDictionary<string, string>> ResolveAsync(
        IEnumerable<string> notations,
        KeeperSecretsManagerOptions options,
        CancellationToken cancellationToken = default)
    {
        var notationList = notations.Distinct().ToList();
        var uids = notationList.Select(ExtractUid).Distinct().ToArray();

        var smOptions = new SecretsManagerOptions(options.Storage);
        var secrets = await _client.GetSecretsAsync(smOptions, uids, cancellationToken).ConfigureAwait(false);

        var returnedUids = secrets.Records.Select(r => r.RecordUid).ToHashSet();
        var missingUids = uids.Where(u => !returnedUids.Contains(u)).ToList();
        if (missingUids.Count > 0)
        {
            throw new KeeperResolutionException(
                $"Keeper record(s) not found: {string.Join(", ", missingUids)}. " +
                "Verify the UID(s) are correct and the application's Keeper credentials have access to them.");
        }

        var result = new Dictionary<string, string>(notationList.Count);
        foreach (var notation in notationList)
        {
            try
            {
                result[notation] = Notation.GetValue(secrets, notation);
            }
            catch (Exception ex)
            {
                throw new KeeperResolutionException(
                    $"Failed to resolve Keeper notation '{notation}': {ex.Message}", ex);
            }
        }

        return result;
    }

    internal static string ExtractUid(string notation)
    {
        const string prefix = "keeper://";
        if (!notation.StartsWith(prefix, StringComparison.Ordinal))
        {
            throw new FormatException($"'{notation}' is not a valid keeper:// notation reference.");
        }

        var afterPrefix = notation.AsSpan(prefix.Length);
        var slashIndex = afterPrefix.IndexOf('/');
        return slashIndex < 0 ? afterPrefix.ToString() : afterPrefix[..slashIndex].ToString();
    }
}
