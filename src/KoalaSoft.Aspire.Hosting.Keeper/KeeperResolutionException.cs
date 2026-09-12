namespace KoalaSoft.Aspire.Hosting.Keeper;

/// <summary>
/// Thrown for any failure to resolve one or more <c>keeper://</c> notation references:
/// a missing record UID, a missing field on a present record, missing bootstrap
/// credentials, or a notation touched during Aspire publish mode.
/// </summary>
public sealed class KeeperResolutionException : Exception
{
    public KeeperResolutionException(string message) : base(message) { }
    public KeeperResolutionException(string message, Exception innerException) : base(message, innerException) { }
}
