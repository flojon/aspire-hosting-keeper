using System.Runtime.CompilerServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Lifecycle;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace KoalaSoft.Aspire.Hosting.Keeper;

public static class KeeperSecretsManagerBuilderExtensions
{
    private const string HookNotRegisteredMessage =
        "AddKeeperSecret requires AddKeeperSecrets(...) to be called first on the same IDistributedApplicationBuilder.";

    internal static readonly ConditionalWeakTable<IDistributedApplicationBuilder, KeeperResolutionLifecycleHook> HooksByBuilder = new();

    /// <summary>
    /// Registers Keeper Secrets Manager support on <paramref name="builder"/>: bootstraps the local
    /// device config if needed, registers the explicit-path lifecycle hook, and adds the implicit-path
    /// <see cref="KeeperConfigurationSource"/> to <paramref name="builder"/>'s configuration. Call this
    /// after every configuration source that may contain <c>keeper://</c> values and before any code
    /// reads those values or calls <see cref="AddKeeperSecret"/>.
    /// </summary>
    [AspireExport]
    public static IDistributedApplicationBuilder AddKeeperSecrets(
        this IDistributedApplicationBuilder builder,
        Action<KeeperSecretsManagerOptions>? configure = null)
    {
        var options = new KeeperSecretsManagerOptions();
        configure?.Invoke(options);

        // Publish must never touch local Keeper credentials: a publish-only machine (e.g. CI)
        // may have none, and bootstrapping here would fail before either publish-mode guard below runs.
        if (!builder.ExecutionContext.IsPublishMode)
        {
            KeeperConfigBootstrapper.EnsureBootstrapped(options);
        }

        var resolver = new KeeperSecretResolver(SecretsManagerClientAdapter.Instance);

        var hook = new KeeperResolutionLifecycleHook(resolver, options, isPublishMode: () => builder.ExecutionContext.IsPublishMode);
        HooksByBuilder.Add(builder, hook);
        // AddSingleton(hook), not TryAddLifecycleHook<T>: we need DI to share this exact
        // instance (already stored in HooksByBuilder for AddKeeperSecret), not construct a new one.
        builder.Services.AddSingleton<IDistributedApplicationLifecycleHook>(hook);

        // ConfigurationManager implements IConfigurationBuilder.Add explicitly, so an
        // unqualified call resolves to an unrelated same-named MVC extension method instead.
        ((IConfigurationBuilder)builder.Configuration).Add(new KeeperConfigurationSource(
            resolver,
            options,
            isPublishMode: () => builder.ExecutionContext.IsPublishMode));

        return builder;
    }

    /// <summary>
    /// Creates a secret <see cref="ParameterResource"/> named <paramref name="name"/> whose value
    /// is resolved from the Keeper notation reference <paramref name="notation"/>. The value stays
    /// unresolved until <see cref="KeeperResolutionLifecycleHook"/> runs, before any dependent
    /// resource starts. Requires <c>AddKeeperSecrets</c> to have been called first on the same builder.
    /// </summary>
    [AspireExport]
    public static IResourceBuilder<ParameterResource> AddKeeperSecret(
        this IDistributedApplicationBuilder builder,
        [ResourceName] string name,
        string notation)
    {
        // Fail fast at the call site, not lazily inside BeforeStartAsync; the extracted
        // UID itself is discarded, resolution re-extracts it later.
        KeeperSecretResolver.ExtractUid(notation);

        var resource = new ParameterResource(
            name,
            _ => HooksByBuilder.TryGetValue(builder, out var hook)
                ? hook.ResolvedValues[notation]
                : throw new InvalidOperationException(HookNotRegisteredMessage),
            secret: true);

        return builder.AddResource(resource)
            .WithAnnotation(new KeeperParameterReferenceAnnotation(notation));
    }
}
