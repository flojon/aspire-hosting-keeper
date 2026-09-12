using System.Runtime.CompilerServices;
using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;

namespace KoalaSoft.Aspire.Hosting.Keeper;

public static class KeeperSecretsManagerBuilderExtensions
{
    private const string HookNotRegisteredMessage =
        "AddKeeperSecret requires AddKeeperSecrets(...) to be called first on the same IDistributedApplicationBuilder.";

    internal static readonly ConditionalWeakTable<IDistributedApplicationBuilder, KeeperResolutionLifecycleHook> HooksByBuilder = new();

    /// <summary>
    /// Creates a secret <see cref="ParameterResource"/> named <paramref name="name"/> whose value
    /// is resolved from the Keeper notation reference <paramref name="notation"/>. The value stays
    /// unresolved until <see cref="KeeperResolutionLifecycleHook"/> runs, before any dependent
    /// resource starts. Requires <c>AddKeeperSecrets</c> to have been called first on the same builder.
    /// </summary>
    public static IResourceBuilder<ParameterResource> AddKeeperSecret(
        this IDistributedApplicationBuilder builder,
        string name,
        string notation)
    {
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
