using Aspire.Hosting.ApplicationModel;

namespace KoalaSoft.Aspire.Hosting.Keeper;

/// <summary>
/// Marks a <see cref="ParameterResource"/> as sourced from a Keeper notation reference.
/// Consulted by <see cref="KeeperResolutionLifecycleHook"/> and inspectable by publishing tools.
/// </summary>
public sealed class KeeperParameterReferenceAnnotation(string notation) : IResourceAnnotation
{
    public string Notation { get; } = notation;
}
