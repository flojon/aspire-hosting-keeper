using Microsoft.Extensions.Configuration;

namespace KoalaSoft.Aspire.Hosting.Keeper;

/// <summary>
/// Adds lazy resolution of bare <c>keeper://</c> strings already present in
/// <paramref name="builder"/>'s previously-registered configuration sources. Must be added
/// after every source that might contain <c>keeper://</c> values.
/// </summary>
internal sealed class KeeperConfigurationSource : IConfigurationSource
{
    private readonly KeeperSecretResolver _resolver;
    private readonly KeeperSecretsManagerOptions _options;
    private readonly Func<bool> _isPublishMode;

    public KeeperConfigurationSource(KeeperSecretResolver resolver, KeeperSecretsManagerOptions options, Func<bool> isPublishMode)
    {
        _resolver = resolver;
        _options = options;
        _isPublishMode = isPublishMode;
    }

    public IConfigurationProvider Build(IConfigurationBuilder builder)
    {
        // IConfigurationSource.Build only receives the builder, not already-Load()-ed provider
        // data, so inventory keeper:// values via a temporary, throwaway root built from the
        // sources registered so far. The real sources on `builder` are untouched here and load
        // normally afterward; this makes every prior source's Load() run twice, which is a
        // no-op for the side-effect-free sources Aspire AppHosts use (JSON files, env vars).
        // builder.Sources already includes this instance (it was Add()-ed before Build() runs);
        // re-adding it here would recurse into this same Build() call forever.
        var inventoryBuilder = new ConfigurationBuilder();
        foreach (var existingSource in builder.Sources)
        {
            if (ReferenceEquals(existingSource, this))
            {
                continue;
            }

            inventoryBuilder.Add(existingSource);
        }
        var inventoryRoot = inventoryBuilder.Build();

        var keeperEntries = new Dictionary<string, string>();
        CollectKeeperValues(inventoryRoot, keeperEntries);

        return new KeeperConfigurationProvider(_resolver, _options, _isPublishMode, keeperEntries);
    }

    private static void CollectKeeperValues(IConfigurationRoot root, Dictionary<string, string> into)
    {
        foreach (var section in root.AsEnumerable())
        {
            if (section.Value is { } value && value.StartsWith("keeper://", StringComparison.Ordinal))
            {
                into[section.Key] = value;
            }
        }
    }
}
