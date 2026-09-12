using Microsoft.Extensions.Configuration;

namespace KoalaSoft.Aspire.Hosting.Keeper;

/// <summary>
/// Resolves its recorded <c>keeper://</c> keys on the first <see cref="TryGet"/> touch, in one
/// batched call, then caches results for the provider's lifetime. Throws instead of resolving
/// or passing through a literal notation string while Aspire is in publish mode.
/// </summary>
internal sealed class KeeperConfigurationProvider : ConfigurationProvider
{
    private readonly KeeperSecretResolver _resolver;
    private readonly KeeperSecretsManagerOptions _options;
    private readonly Func<bool> _isPublishMode;
    private readonly IReadOnlyDictionary<string, string> _notationsByKey;
    private readonly SemaphoreSlim _resolveLock = new(1, 1);
    private volatile bool _resolved;

    public KeeperConfigurationProvider(
        KeeperSecretResolver resolver,
        KeeperSecretsManagerOptions options,
        Func<bool> isPublishMode,
        IReadOnlyDictionary<string, string> notationsByKey)
    {
        _resolver = resolver;
        _options = options;
        _isPublishMode = isPublishMode;
        _notationsByKey = notationsByKey;
    }

    public override bool TryGet(string key, out string? value)
    {
        if (!_notationsByKey.TryGetValue(key, out var notation))
        {
            return base.TryGet(key, out value);
        }

        if (_isPublishMode())
        {
            throw new KeeperResolutionException(
                $"Configuration key '{key}' holds an unresolved Keeper reference ('{notation}') that " +
                "was touched during 'aspire publish'. Implicit keeper:// config-scanning is dev-run-only; " +
                "convert this reference to an explicit AddKeeperSecret(...) parameter, which publish handles correctly.");
        }

        EnsureResolved();
        return base.TryGet(key, out value);
    }

    private void EnsureResolved()
    {
        if (_resolved)
        {
            return;
        }

        _resolveLock.Wait();
        try
        {
            if (_resolved)
            {
                return; // another thread finished while we waited on the semaphore
            }

            // TryGet is synchronous but resolution is async; offload to the thread pool so this
            // cannot deadlock even under a future caller's ambient SynchronizationContext.
            var resolved = Task.Run(() => _resolver.ResolveAsync(_notationsByKey.Values, _options)).GetAwaiter().GetResult();

            foreach (var (key, notation) in _notationsByKey)
            {
                Set(key, resolved[notation]);
            }

            _resolved = true;
        }
        finally
        {
            _resolveLock.Release();
        }
    }
}
