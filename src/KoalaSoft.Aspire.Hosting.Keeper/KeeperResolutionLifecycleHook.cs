using System.Collections.Concurrent;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;

namespace KoalaSoft.Aspire.Hosting.Keeper;

/// <summary>
/// Explicit-path resolution: collects every <see cref="KeeperParameterReferenceAnnotation"/> in
/// the app model, resolves them all in one batched call, and caches the results so each
/// annotated <see cref="ParameterResource"/>'s value-callback can read them synchronously.
/// </summary>
internal sealed class KeeperResolutionLifecycleHook : IDistributedApplicationLifecycleHook
{
    private readonly KeeperSecretResolver _resolver;
    private readonly KeeperSecretsManagerOptions _options;
    private readonly Func<bool> _isPublishMode;

    public ConcurrentDictionary<string, string> ResolvedValues { get; } = new();

    public KeeperResolutionLifecycleHook(KeeperSecretResolver resolver, KeeperSecretsManagerOptions options, Func<bool> isPublishMode)
    {
        _resolver = resolver;
        _options = options;
        _isPublishMode = isPublishMode;
    }

    public async Task BeforeStartAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken = default)
    {
        // Publish must never resolve real secrets; leave ResolvedValues empty and do nothing.
        if (_isPublishMode())
        {
            return;
        }

        var notations = appModel.Resources
            .SelectMany(r => r.TryGetAnnotationsOfType<KeeperParameterReferenceAnnotation>(out var annotations)
                ? annotations
                : Enumerable.Empty<KeeperParameterReferenceAnnotation>())
            .Select(a => a.Notation)
            .Distinct()
            .ToList();

        if (notations.Count == 0)
        {
            return;
        }

        var resolved = await _resolver.ResolveAsync(notations, _options, cancellationToken).ConfigureAwait(false);
        foreach (var (notation, value) in resolved)
        {
            ResolvedValues[notation] = value;
        }
    }

    public Task AfterEndpointsAllocatedAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task AfterResourcesCreatedAsync(DistributedApplicationModel appModel, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
