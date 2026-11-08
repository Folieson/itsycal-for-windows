using System.Globalization;
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
/// in <c>secrets.dat</c> and every account gets its own
/// <see cref="ProtectedData.Protect(byte[], byte[]?, DataProtectionScope)"/> record encrypted with
/// <see cref="DataProtectionScope.CurrentUser"/> and per-account entropy derived from
/// <see cref="SecretEntropy"/> plus the account id. A copied secrets file is therefore useless to
/// another Windows user, one damaged record cannot take the other accounts' secrets down, and the
/// account JSON never contains a token or a token-bearing feed address.
/// </para>
/// <para>
/// Writes go to a sibling temporary file, are flushed to disk and then moved over the target
/// (atomic replace), so a crash cannot leave half-written JSON. Every operation is serialized by
/// an in-process lock and a cross-process named mutex, because the Command Palette extension and
/// the standalone flyout can run at the same time. The directory and both files get a protected
/// ACL for the current user only; this is defense in depth on top of the per-user
/// <c>%LOCALAPPDATA%</c> profile.
/// </para>
/// <para>
/// Unreadable, corrupt or truncated files read as "empty" instead of taking the settings surface
/// or a sync down. Before an unreadable file is overwritten it is moved aside as
/// <c>&lt;name&gt;.corrupt-&lt;timestamp&gt;-&lt;token&gt;.bak</c>, so a corrupt store never silently
/// destroys every secret. Account metadata with a token-bearing ICS remote id is redacted on read
/// (such a value can only come from a hand-edited file or an older development build).
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

    private const int CurrentVersion = 2;

    /// <summary>How long a store operation waits for the cross-process lock.</summary>
    private static readonly TimeSpan MutexTimeout = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Application-specific DPAPI entropy. It binds the ciphertext to this feature; changing it
    /// makes existing secrets unreadable, so it is versioned. A per-account digest is derived from
    /// it in <see cref="DeriveEntropy"/>.
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
    private readonly string _mutexName;
    private readonly TimeProvider _timeProvider;
    private bool _accountsUnreadable;
    private bool _secretsUnreadable;

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
        _mutexName = MutexNameFor(_directory);

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
        return WithStoreLock(() => LoadAccounts()
            .OrderBy(static account => account.CreatedAt)
            .ThenBy(static account => account.Id, StringComparer.Ordinal)
            .ToArray());
    }

    /// <inheritdoc />
    public ProviderAccount? Find(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        return WithStoreLock(() =>
            LoadAccounts().FirstOrDefault(account => SameId(account.Id, accountId)));
    }

    /// <inheritdoc />
    public ProviderAccount Add(ProviderAccount account)
    {
        ArgumentNullException.ThrowIfNull(account);

        return WithStoreLock(() =>
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
        });
    }

    /// <inheritdoc />
    public bool Remove(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        return WithStoreLock(() =>
        {
            var accounts = LoadAccounts();
            var index = accounts.FindIndex(account => SameId(account.Id, accountId));
            if (index < 0)
            {
                return false;
            }

            // Remove the secret first: an orphaned account without a secret shows an honest
            // "not configured" state, while an orphaned secret would outlive its account.
            RemoveSecretUnlocked(accountId);
            accounts.RemoveAt(index);
            SaveAccounts(accounts);
            return true;
        });
    }

    /// <inheritdoc />
    public bool SetEnabled(string accountId, bool enabled)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        return WithStoreLock(() =>
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
        });
    }

    /// <inheritdoc />
    public bool MarkSynced(string accountId, DateTimeOffset lastSyncAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        return WithStoreLock(() =>
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
        });
    }

    /// <inheritdoc />
    public bool TryGet(string accountId, out ProviderSecret? secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        ProviderSecret? found = null;
        var result = WithStoreLock(() =>
        {
            var secrets = LoadSecrets();
            var exists = secrets.TryGetValue(accountId, out var value);
            found = value;
            return exists;
        });

        secret = found;
        return result;
    }

    /// <inheritdoc />
    public Task<ProviderSecret?> GetAsync(string accountId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(TryGet(accountId, out var secret) ? secret : null);
    }

    /// <inheritdoc />
    public bool Save(string accountId, ProviderSecret secret)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);
        ArgumentNullException.ThrowIfNull(secret);

        return WithStoreLock(() =>
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
        });
    }

    /// <inheritdoc />
    bool IProviderSecretStore.Remove(string accountId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accountId);

        return WithStoreLock(() => RemoveSecretUnlocked(accountId));
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

        if (!string.IsNullOrWhiteSpace(account.RemoteId))
        {
            if (account.Kind == ProviderKind.IcsSubscription &&
                !IcsFeedAddress.IsSafeRemoteId(account.RemoteId))
            {
                throw new ArgumentException(
                    "An ICS account may store only the display-safe scheme and host in RemoteId; " +
                    "keep the full feed address in the provider secret.",
                    nameof(account));
            }

            if (Uri.TryCreate(account.RemoteId, UriKind.Absolute, out var remoteUri) &&
                !string.IsNullOrEmpty(remoteUri.UserInfo))
            {
                throw new ArgumentException(
                    "A remote id must not contain embedded credentials.",
                    nameof(account));
            }
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
                _accountsUnreadable = true;
                return [];
            }

            var document = JsonSerializer.Deserialize<AccountsDocument>(json, JsonOptions);
            if (document?.Accounts is null)
            {
                _accountsUnreadable = true;
                return [];
            }

            return document.Accounts
                .Where(static account => account is not null)
                .Select(SanitizeForRead)
                .ToList();
        }
        catch (Exception)
        {
            _accountsUnreadable = true;
            return [];
        }
#pragma warning restore CA1031
    }

    private static ProviderAccount SanitizeForRead(ProviderAccount account)
    {
        // An older development build stored the full feed URL here. Never surface it again; the
        // original file stays untouched until the next write quarantines and replaces it.
        return account.Kind == ProviderKind.IcsSubscription && !IcsFeedAddress.IsSafeRemoteId(account.RemoteId)
            ? account with { RemoteId = null }
            : account;
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
            var json = File.ReadAllText(_secretsPath, Encoding.UTF8);
            if (string.IsNullOrWhiteSpace(json))
            {
                _secretsUnreadable = true;
                return empty;
            }

            var document = JsonSerializer.Deserialize<SecretsDocument>(json, JsonOptions);
            if (document?.Secrets is null)
            {
                _secretsUnreadable = true;
                return empty;
            }

            var secrets = new Dictionary<string, ProviderSecret>(StringComparer.Ordinal);
            foreach (var (accountId, protectedValue) in document.Secrets)
            {
                if (TryUnprotect(accountId, protectedValue, out var secret))
                {
                    secrets[accountId] = secret;
                }
                else
                {
                    // Keep the healthy records; quarantine the original file before overwriting
                    // it so the damaged record can still be recovered.
                    _secretsUnreadable = true;
                }
            }

            return secrets;
        }
        catch (Exception)
        {
            _secretsUnreadable = true;
            return empty;
        }
#pragma warning restore CA1031
    }

    private void SaveAccounts(IReadOnlyList<ProviderAccount> accounts)
    {
        QuarantineIfUnreadable(ref _accountsUnreadable, _accountsPath);

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
        QuarantineIfUnreadable(ref _secretsUnreadable, _secretsPath);

        var document = new SecretsDocument
        {
            Version = CurrentVersion,
            Secrets = new Dictionary<string, string>(StringComparer.Ordinal),
        };

        foreach (var (accountId, secret) in secrets)
        {
            if (secret.IsEmpty)
            {
                continue;
            }

            var plainBytes = JsonSerializer.SerializeToUtf8Bytes(secret, JsonOptions);
            try
            {
                var protectedBytes = ProtectedData.Protect(
                    plainBytes,
                    DeriveEntropy(accountId),
                    DataProtectionScope.CurrentUser);
                document.Secrets[accountId] = Convert.ToBase64String(protectedBytes);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }

        var bytes = JsonSerializer.SerializeToUtf8Bytes(document, JsonOptions);
        WriteAtomic(_secretsPath, bytes);
    }

    private void QuarantineIfUnreadable(ref bool unreadable, string path)
    {
        if (!unreadable)
        {
            return;
        }

        unreadable = false;
        if (!File.Exists(path))
        {
            return;
        }

        var quarantinePath = string.Concat(
            path,
            ".corrupt-",
            _timeProvider.GetUtcNow().ToString("yyyyMMddHHmmssfff", CultureInfo.InvariantCulture),
            "-",
            Guid.NewGuid().ToString("N")[..8],
            ".bak");

#pragma warning disable CA1031 // Quarantine is best effort; the write still has to succeed.
        try
        {
            File.Move(path, quarantinePath);
        }
        catch (Exception)
        {
            try
            {
                File.Copy(path, quarantinePath, overwrite: false);
            }
            catch (Exception)
            {
                // If the file cannot be moved or copied, the atomic replace still writes the new
                // content; no secret is written to the quarantine path by this process.
            }
        }
#pragma warning restore CA1031
    }

    private static bool TryUnprotect(string accountId, string protectedValue, out ProviderSecret secret)
    {
        secret = null!;

#pragma warning disable CA1031 // One damaged record reads as "no secret" instead of failing the store.
        try
        {
            var cipherBytes = Convert.FromBase64String(protectedValue);
            var plainBytes = ProtectedData.Unprotect(
                cipherBytes,
                DeriveEntropy(accountId),
                DataProtectionScope.CurrentUser);

            try
            {
                var value = JsonSerializer.Deserialize<ProviderSecret>(plainBytes, JsonOptions);
                if (value is null)
                {
                    return false;
                }

                secret = value;
                return true;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plainBytes);
            }
        }
        catch (Exception)
        {
            return false;
        }
#pragma warning restore CA1031
    }

    private static byte[] DeriveEntropy(string accountId)
    {
        var accountBytes = Encoding.UTF8.GetBytes(accountId);
        var buffer = new byte[SecretEntropy.Length + accountBytes.Length];
        SecretEntropy.CopyTo(buffer, 0);
        accountBytes.CopyTo(buffer, SecretEntropy.Length);
        return SHA256.HashData(buffer);
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

            TryApplyUserOnlyAcl(temporaryPath);
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
                // Best effort cleanup; a leftover temp file never contains account data in
                // plaintext: account metadata is public and the secrets blob is already encrypted.
            }
        }
    }

    private void EnsureDirectory()
    {
        System.IO.Directory.CreateDirectory(_directory);
        TryApplyUserOnlyAcl(_directory);
    }

    private T WithStoreLock<T>(Func<T> action)
    {
        using var mutex = TryCreateMutex();
        var acquired = mutex is null;

        if (mutex is not null)
        {
            try
            {
                acquired = mutex.WaitOne(MutexTimeout);
            }
            catch (AbandonedMutexException)
            {
                acquired = true;
            }
        }

        if (!acquired)
        {
            throw new IOException("The provider account store is locked by another process.");
        }

        try
        {
            lock (_gate)
            {
                return action();
            }
        }
        finally
        {
            if (mutex is not null)
            {
                try
                {
                    mutex.ReleaseMutex();
                }
                catch (ApplicationException)
                {
                    // The named mutex was abandoned and re-acquired; nothing left to release.
                }
            }
        }
    }

    private Mutex? TryCreateMutex()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

#pragma warning disable CA1031 // A restrictive policy must not take the settings surface down.
        try
        {
            return new Mutex(initiallyOwned: false, _mutexName);
        }
        catch (Exception)
        {
            return null;
        }
#pragma warning restore CA1031
    }

    private static string MutexNameFor(string directory)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(directory.ToUpperInvariant()));
        return "Local\\ItsycalForWindows.ProviderAccounts." + Convert.ToHexString(hash, 0, 16);
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

        /// <summary>Account id to base64(DPAPI ciphertext of the serialized secret).</summary>
        public Dictionary<string, string> Secrets { get; set; } = new(StringComparer.Ordinal);
    }
}
