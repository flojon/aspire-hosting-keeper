using SecretsManager;

namespace KoalaSoft.Aspire.Hosting.Keeper;

/// <summary>
/// Configuration for resolving <c>keeper://</c> notation references via Keeper Secrets Manager.
/// </summary>
public sealed class KeeperSecretsManagerOptions
{
    /// <summary>
    /// Default location of the local encrypted Keeper config file, mirroring the Keeper CLI's own default.
    /// </summary>
    public static string DefaultConfigPath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
        ".keeper",
        "aspire-config.json");

    private string _configPath = DefaultConfigPath;
    private IKeyValueStorage _storage = new LocalConfigStorage(DefaultConfigPath);

    /// <summary>
    /// Path backing the default <see cref="LocalConfigStorage"/>. Setting this replaces
    /// <see cref="Storage"/> with a new <see cref="LocalConfigStorage"/> at the given path.
    /// Has no effect if <see cref="Storage"/> is subsequently set directly.
    /// </summary>
    public string ConfigPath
    {
        get => _configPath;
        set
        {
            _configPath = value;
            Storage = new LocalConfigStorage(value);
            StorageIsManagedByConfigPath = true;
        }
    }

    /// <summary>
    /// Storage backing the Keeper SDK's local device credentials. Defaults to a
    /// <see cref="LocalConfigStorage"/> at <see cref="ConfigPath"/>.
    /// </summary>
    public IKeyValueStorage Storage
    {
        get => _storage;
        set
        {
            _storage = value;
            StorageIsManagedByConfigPath = false;
        }
    }

    /// <summary>
    /// True when <see cref="Storage"/> was produced by the <see cref="ConfigPath"/> setter (or is
    /// still the untouched default) rather than assigned directly by the caller. Distinguishes
    /// "the default/managed <see cref="LocalConfigStorage"/> at <see cref="ConfigPath"/>" from
    /// "a caller-supplied <see cref="LocalConfigStorage"/> at some other path" — both are the same
    /// runtime type, so <see cref="KeeperConfigBootstrapper"/> cannot otherwise tell them apart.
    /// </summary>
    internal bool StorageIsManagedByConfigPath { get; private set; } = true;

    /// <summary>
    /// One-time token used only to bootstrap <see cref="Storage"/> on first run. Not required
    /// once the local config already contains device credentials.
    /// </summary>
    public string? OneTimeToken { get; set; }
}
