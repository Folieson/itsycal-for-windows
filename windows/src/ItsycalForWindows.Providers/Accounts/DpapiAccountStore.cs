using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ItsycalForWindows.Providers;

/// <summary>
/// Default account and secret store: a JSON account registry plus a separate DPAPI-encrypted
/// secrets file under <c>%LOCALAPPDATA%\ItsycalForWindows\accounts</c>.
/// </summary>
/// <remarks>
/// <para>
/// The two files are deliberately separate: <c>accounts.json</c> contains only the public
/// <see cref="ProviderAccount"/> metadata and can be inspected, backed up or exported; secrets live
/// in <c>secrets.dat</c> and are encrypted as one blob with
/// <see cref="ProtectedData.Protect(byte[], byte[]?, DataProtectionScope)"/> using
/// <see cref="DataProtectionScope.CurrentUser"/> and application entropy. A copied secrets file is
/// therefore useless to another Windows user and the account JSON never contains a token.
/// </para>
/// <para>
/// Writes go to a sibling temporary file, are flushed to disk and then moved over the target
/// (atomic replace), so a crash cannot leave half-written JSON. The directory and both files get a
/// protected ACL for the current user only; this is defense in depth on top of the per-user
/// <c>%LOCALAPPDATA%</c> profile. Unreadable, corrupt or truncated files read as "empty" instead of
/// taking the settings surface or a sync down.
/// </para>
/// <para>
/// The store has no logging path and never writes secret material to a file name, exception
/// message or <see cref="object.ToString"/>.
/// </para>
/// </remarks>
public sealed class DpapiAccountStore : IAccountStore, IProviderSecretStore
{
    /// <summary>Folder under <c>%LOCALAPPDATA%\ItsycalForWindows</c> that holds both files.</summary>
    public const string FolderName = "accounts";

    /// <summary>File name of the non-secret account registry.</summary>
    public const string AccountsFileName = "accounts.json";

    /// <summary>File name of the DPAPI-encrypted secret document.</summary>
    public const string SecretsFileName = "secrets.dat";

    private const int CurrentVersion = 1;

    /// <summary>
    /// Application-specific DPAPI entropy. It binds the ciphertext to this feature; changing it
    /// makes existing secrets unreadable, so it is versioned.
    /// </summary>
    private static readonly byte[] SecretEntropy = Encoding.UTF8.GetBytes("ItsycalForWindows.ProviderSecrets.v1");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly object _gate = new();
    private readonly string _directory;
    private readonly string _accountsPath;
    private readonly string _secretsPath;
    private readonly TimeProvider _timeProvider;

    /// <summary>Creates a store at the default per-user location.</summary>
    public DpapiAccountStore()
        : this(DefaultDirectory)
    {
    }

    /// <summary>Creates a store rooted at <paramref name="directory"/> (used by tests and callers
    /// that need a non-default profile).</summary>
    public DpapiAccountStore(string directory)
        : this(directory, TimeProvider.System)
    {
    }

    /// <summary>Creates a store with an injectable clock, primarily for deterministic tests.</summary>
    public DpapiAccountStore(string directory, TimeProvider timeProvider)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        _directory = Path.GetFullPath(directory);
        _accountsPath = Path.Combine(_directory, AccountsFileName);
        _secretsPath = Path.Combine(_directory, SecretsFileName);
        _timeProvider = timeProvider ?? TimeProvider.System;

        EnsureDirectory();
    }

    /// <summary>The default folder: <c>%LOCALAPPDATA%\ItsycalForWindows\accounts</c>.</summary>
    public static string DefaultDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "ItsycalForWindows",
        FolderName);

    /// <summary>The folder this store owns.</summary>
    public string Directory => _directory;

    /// <inheritdoc />
    public IReadOnlyList<ProviderAccount> List()
    {
        lock (_gate)
        {
            return LoadAccounts()
                .OrderBy(static account => account.CreatedAt)
                .ThenBy(static account => account.Id, StringComparer.Ordinal)
                .ToArray();
        }
    }

    /// <inheritdoc />
    public ProviderAccount? Find(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        lock (_gate)
        {
            return LoadAccounts().FirstOrDefault(account => SameId(account.Id, accountId));
        }
    }

    /// <inheritdoc />
    public ProviderAccount Add(ProviderAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        lock (_gate)
        {
            var normalized = NormalizeForAdd(account);
            var accounts = LoadAccounts();
            if (accounts.Any(existing => SameId(existing.Id, normalized.Id)))
            {
                throw new InvalidOperationException($"An account with id '{normalized.Id}' already exists.");
            }

            accounts.Add(normalized);
            SaveAccounts(accounts);
            return normalized;
        }
    }

    /// <inheritdoc />
    public bool Remove(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        lock (_gate)
        {
            var accounts = LoadAccounts();
            var removed = accounts.RemoveAll(account => SameId(account.Id, accountId));
            if (removed == 0)
            {
                return false;
            }

            SaveAccounts(accounts);
            RemoveSecretUnlocked(accountId);
            return true;
        }
    }

    /// <inheritdoc />
    public bool SetEnabled(string accountId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        lock (_gate)
        {
            var accounts = LoadAccounts();
            var index = accounts.FindIndex(account => SameId(account.Id, accountId));
            if (index < 0)
            {
                return false;
            }

            accounts[index] = accounts[index] with { Enabled = enabled };
            SaveAccounts(accounts);
            return true;
        }
    }

    /// <inheritdoc />
    public bool MarkSynced(string accountId, DateTimeOffset lastSyncAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        lock (_gate)
        {
            var accounts = LoadAccounts();
            var index = accounts.FindIndex(account => SameId(account.Id, accountId));
            if (index < 0)
            {
                return false;
            }

            accounts[index] = accounts[index].WithLastSync(lastSyncAt);
            SaveAccounts(accounts);
            return true;
        }
    }

    /// <inheritdoc />
    public bool TryGet(string accountId, out ProviderSecret? secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        lock (_gate)
        {
            return LoadSecrets().TryGetValue(accountId, out secret);
        }
    }

    /// <inheritdoc />
    public bool Save(string accountId, ProviderSecret secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(secret);

        lock (_gate)
        {
            var secrets = LoadSecrets();
            if (secret.IsEmpty)
            {
                if (!secrets.Remove(accountId))
                {
                    return false;
                }

                SaveSecrets(secrets);
                return true;
            }

            secrets[accountId] = secret;
            SaveSecrets(secrets);
            return true;
        }
    }

    /// <inheritdoc />
    bool IProviderSecretStore.Remove(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        lock (_gate)
        {
            return RemoveSecretUnlocked(accountId);
        }
    }

    private bool RemoveSecretUnlocked(string accountId)
    {
        var secrets = LoadSecrets();
        if (!secrets.Remove(accountId))
        {
            return false;
        }

        SaveSecrets(secrets);
        return true;
    }

    private ProviderAccount NormalizeForAdd(ProviderAccount account)
    {
        if (string.IsNullOrWhiteSpace(account.Id))
        {
            throw new ArgumentException("The account id is required.", nameof(account));
        }

        if (!Enum.IsDefined(account.Kind))
        {
            throw new ArgumentException($"Unknown provider kind '{account.Kind}'.", nameof(account));
        }

        var displayName = string.IsNullOrWhiteSpace(account.DisplayName)
            ? account.Kind.ToString()
            : account.DisplayName.Trim();

        return account with
        {
            Id = account.Id.Trim(),
            DisplayName = displayName,
            AccountName = string.IsNullOrWhiteSpace(account.AccountName) ? null : account.AccountName.Trim(),
            RemoteId = string.IsNullOrWhiteSpace(account.RemoteId) ? null : account.RemoteId.Trim(),
            CreatedAt = account.CreatedAt == default ? _timeProvider.GetUtcNow() : account.CreatedAt.ToUniversalTime(),
            LastSyncAt = account.LastSyncAt?.ToUniversalTime(),
        };
    }

    private List<ProviderAccount> LoadAccounts()
    {
        if (!File.Exists(_accountsPath))
        {
            return [];
        }

#pragma warning disable CA1031 // A corrupt account file must not crash the settings surface.
        try
        {
            var json = File.ReadAllText(_accountsPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                return [];
            }

            var document = JsonSerializer.Deserialize<AccountsDocument>(json, JsonOptions);
            return document?.Accounts ?? [];
        }
        catch (Exception)
        {
            return [];
        }
#pragma warning restore CA1031
    }

    private Dictionary<string, ProviderSecret> LoadSecrets()
    {
        var empty = new Dictionary<string, ProviderSecret>(StringComparer.Ordinal);
        if (!File.Exists(_secretsPath))
        {
            return empty;
        }

#pragma warning disable CA1031 // DPAPI, IO and JSON failures all mean "no readable secrets".
        try
        {
            var protectedBytes = File.ReadAllBytes(_secretsPath);
            if (protectedBytes.Length == 0)
            {
                return empty;
            }

            var plainBytes = ProtectedData.Unprotect(protectedBytes, SecretEntropy, DataProtectionScope.CurrentUser);
            var document = JsonSerializer.Deserialize<SecretsDocument>(plainBytes, JsonOptions);
            return document?.Secrets is { } secrets
                ? new Dictionary<string, ProviderSecret>(secrets, StringComparer.Ordinal)
                : empty;
        }
        catch (Exception)
        {
            return empty;
        }
#pragma warning restore CA1031
    }

    private void SaveAccounts(IReadOnlyList<ProviderAccount> accounts)
    {
        var document = new AccountsDocument
        {
            Version = CurrentVersion,
            Accounts = accounts.ToList(),
        };

        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        WriteAtomic(_accountsPath, bytes);
    }

    private void SaveSecrets(Dictionary<string, ProviderSecret> secrets)
    {
        var document = new SecretsDocument
        {
            Version = CurrentVersion,
            Secrets = secrets,
        };

        var plainBytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        var protectedBytes = ProtectedData.Protect(plainBytes, SecretEntropy, DataProtectionScope.CurrentUser);
        WriteAtomic(_secretsPath, protectedBytes);
    }

    private static void WriteAtomic(string path, byte[] bytes)
    {
        var temporaryPath = path + "." + Guid.NewGuid().ToString("N") + ".tmp";

        try
        {
            using (var stream = new FileStream(
                temporaryPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                bufferSize: 4096,
                FileOptions.WriteThrough))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }

            File.Move(temporaryPath, path, overwrite: true);
            TryApplyUserOnlyAcl(path);
        }
        finally
        {
            try
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
            catch (Exception)
            {
                // Best effort cleanup; a leftover temp file never contains an account secret in
                // plaintext (the secrets blob is already encrypted when it reaches this method).
            }
        }
    }

    private void EnsureDirectory()
    {
        System.IO.Directory.CreateDirectory(_directory);
        TryApplyUserOnlyAcl(_directory);
    }

    private static void TryApplyUserOnlyAcl(string path)
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

#pragma warning disable CA1031 // ACL hardening is best effort; the profile is already user-scoped.
        try
        {
            var user = WindowsIdentity.GetCurrent().User;
            if (user is null)
            {
                return;
            }

            if (System.IO.Directory.Exists(path))
            {
                var directorySecurity = new DirectorySecurity();
                directorySecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                directorySecurity.AddAccessRule(new FileSystemAccessRule(
                    user,
                    FileSystemRights.FullControl,
                    InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
                    PropagationFlags.None,
                    AccessControlType.Allow));
                directorySecurity.SetOwner(user);
                new DirectoryInfo(path).SetAccessControl(directorySecurity);
            }
            else if (File.Exists(path))
            {
                var fileSecurity = new FileSecurity();
                fileSecurity.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
                fileSecurity.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
                fileSecurity.SetOwner(user);
                new FileInfo(path).SetAccessControl(fileSecurity);
            }
        }
        catch (Exception)
        {
            // Never fail the store because a profile policy prevents tightening the ACL.
        }
#pragma warning restore CA1031
    }

    private static bool SameId(string left, string right) =>
        string.Equals(left, right, StringComparison.Ordinal);

    private sealed class AccountsDocument
    {
        public int Version { get; set; }

        public List<ProviderAccount> Accounts { get; set; } = [];
    }

    private sealed class SecretsDocument
    {
        public int Version { get; set; }

        public Dictionary<string, ProviderSecret> Secrets { get; set; } = new(StringComparer.Ordinal);
    }
}
