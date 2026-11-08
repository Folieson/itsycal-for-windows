using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ItsycalForWindows.Providers;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for <see cref="DpapiAccountStore"/>: account CRUD, persistence, DPAPI encryption at rest,
/// corrupt/truncated files, quarantine, atomic writes, cross-instance serialization and the
/// user-only ACL.
/// </summary>
public sealed class DpapiAccountStoreTests
{
    private const string FeedToken = "SECRET-FEED-TOKEN-42";

    [Fact]
    public void Add_List_Find_Remove_RoundTripsAccounts()
    {
        using var root = new ProviderTestRoot();
        var store = new DpapiAccountStore(root.Combine("accounts"));

        var first = store.Add(ProviderAccount.Create(ProviderKind.Google, "Personal", "me@example.com", "primary"));
        var (teamAccount, teamSecret) = ProviderAccount.CreateIcsSubscription(
            "Team feed",
            $"https://example.com/private/{FeedToken}/team.ics");
        var second = store.Add(teamAccount);
        store.Save(second.Id, teamSecret);

        var list = store.List();
        Assert.Equal(2, list.Count);
        Assert.Contains(list, account => account.Id == first.Id);
        Assert.Contains(list, account => account.Id == second.Id);
        Assert.Equal("Personal", store.Find(first.Id)?.DisplayName);

        // The reloaded account is metadata only and the secret is fetched separately.
        Assert.Equal("https://example.com", store.Find(second.Id)?.RemoteId);
        Assert.True(store.TryGet(second.Id, out var loadedSecret));
        Assert.Equal(teamSecret.Resource, loadedSecret?.Resource);

        Assert.True(store.Remove(first.Id));
        Assert.Null(store.Find(first.Id));
        Assert.Single(store.List());
        Assert.False(store.Remove(first.Id));
    }

    [Fact]
    public void SavedIcsSubscription_ReloadsWithDisplaySafeMetadataAndItsSecret()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        var store = new DpapiAccountStore(directory);
        var (account, secret) = ProviderAccount.CreateIcsSubscription(
            "Team feed",
            $"https://example.com/private/{FeedToken}/feed.ics");
        store.Add(account);
        store.Save(account.Id, secret);

        var reloaded = new DpapiAccountStore(directory);
        var loaded = reloaded.Find(account.Id);

        Assert.NotNull(loaded);
        Assert.Equal("https://example.com", loaded!.RemoteId);
        Assert.DoesNotContain(FeedToken, loaded.ToString(), StringComparison.Ordinal);
        Assert.True(reloaded.TryGet(account.Id, out var loadedSecret));
        Assert.Equal(secret.Resource, loadedSecret!.Resource);
    }

    [Fact]
    public void Add_RejectsDuplicateIdInvalidKindAndTokenizedIcsRemoteId()
    {
        using var root = new ProviderTestRoot();
        var store = new DpapiAccountStore(root.Combine("accounts"));
        var account = ProviderAccount.Create(ProviderKind.Google, "Work", id: "same-id");
        store.Add(account);

        Assert.Throws<InvalidOperationException>(() => store.Add(account));
        Assert.Throws<ArgumentException>(() =>
            store.Add(ProviderAccount.Create((ProviderKind)999, "Unknown")));
        Assert.Throws<ArgumentException>(() => store.Add(ProviderAccount.Create(
            ProviderKind.IcsSubscription,
            "Leaky",
            remoteId: $"https://example.com/private/{FeedToken}/feed.ics")));
        Assert.Throws<ArgumentException>(() => store.Add(ProviderAccount.Create(
            ProviderKind.GenericCalDav,
            "Creds",
            remoteId: "https://user:password@example.com/dav/")));
    }

    [Fact]
    public void SetEnabledAndMarkSynced_PersistAcrossInstances()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        var store = new DpapiAccountStore(directory);
        var account = store.Add(ProviderAccount.Create(ProviderKind.MicrosoftGraph, "Work", "user@contoso.com"));

        var syncedAt = new DateTimeOffset(2026, 11, 8, 12, 0, 0, TimeSpan.Zero);
        Assert.True(store.SetEnabled(account.Id, false));
        Assert.True(store.MarkSynced(account.Id, syncedAt));

        var reloaded = new DpapiAccountStore(directory).Find(account.Id);
        Assert.NotNull(reloaded);
        Assert.False(reloaded!.Enabled);
        Assert.Equal(syncedAt, reloaded.LastSyncAt);
        Assert.False(store.SetEnabled("missing", true));
        Assert.False(store.MarkSynced("missing", syncedAt));
    }

    [Fact]
    public void AccountsFile_NeverContainsTheIcsFeedAddressOrToken()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        var store = new DpapiAccountStore(directory);
        var (account, secret) = ProviderAccount.CreateIcsSubscription(
            "Team feed",
            $"https://example.com/private/{FeedToken}/team.ics?token={FeedToken}");
        store.Add(account);
        store.Save(account.Id, secret);

        var accountsPath = Path.Combine(directory, DpapiAccountStore.AccountsFileName);
        var rawAccounts = File.ReadAllText(accountsPath);
        Assert.DoesNotContain(FeedToken, rawAccounts, StringComparison.Ordinal);
        Assert.DoesNotContain("private/", rawAccounts, StringComparison.Ordinal);
        Assert.Contains("https://example.com", rawAccounts, StringComparison.Ordinal);
        Assert.DoesNotContain("Resource", rawAccounts, StringComparison.OrdinalIgnoreCase);

        var secretsPath = Path.Combine(directory, DpapiAccountStore.SecretsFileName);
        var rawSecrets = File.ReadAllText(secretsPath);
        Assert.DoesNotContain(FeedToken, rawSecrets, StringComparison.Ordinal);
        Assert.DoesNotContain("private/", rawSecrets, StringComparison.Ordinal);
    }

    [Fact]
    public void Secrets_ArePerAccountEncryptedAndRoundTrip()
    {
        using var root = new ProviderTestRoot();
        var store = new DpapiAccountStore(root.Combine("accounts"));
        var first = store.Add(ProviderAccount.Create(ProviderKind.Google, "Personal", "me@example.com"));
        var second = store.Add(ProviderAccount.Create(ProviderKind.GenericCalDav, "Nextcloud"));

        const string accessToken = "ya29.SECRET-ACCESS-TOKEN";
        const string password = "SECRET-APP-PASSWORD";
        store.Save(first.Id, new ProviderSecret
        {
            AccessToken = accessToken,
            RefreshToken = "1//SECRET-REFRESH-TOKEN",
            ExpiresAt = new DateTimeOffset(2026, 11, 8, 13, 0, 0, TimeSpan.Zero),
        });
        store.Save(second.Id, new ProviderSecret { Password = password });

        Assert.True(store.TryGet(first.Id, out var firstSecret));
        Assert.Equal(accessToken, firstSecret!.AccessToken);
        Assert.True(store.TryGet(second.Id, out var secondSecret));
        Assert.Equal(password, secondSecret!.Password);
        Assert.Null(firstSecret.Password);

        var rawSecrets = File.ReadAllText(Path.Combine(
            store.Directory,
            DpapiAccountStore.SecretsFileName));
        Assert.DoesNotContain(accessToken, rawSecrets, StringComparison.Ordinal);
        Assert.DoesNotContain(password, rawSecrets, StringComparison.Ordinal);
        Assert.Equal(2, CountSecretRecords(rawSecrets));
    }

    [Fact]
    public async Task GetAsync_ReturnsStoredSecretOrNull()
    {
        using var root = new ProviderTestRoot();
        var store = new DpapiAccountStore(root.Combine("accounts"));
        var account = store.Add(ProviderAccount.Create(ProviderKind.Google, "Personal"));

        Assert.Null(await store.GetAsync("missing"));

        store.Save(account.Id, new ProviderSecret { AccessToken = "token" });
        var secret = await store.GetAsync(account.Id);
        Assert.Equal("token", secret?.AccessToken);

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.GetAsync(account.Id, cancellation.Token));
    }

    [Fact]
    public void EmptySecretAndExplicitSecretRemove_ClearTheEntry()
    {
        using var root = new ProviderTestRoot();
        var store = new DpapiAccountStore(root.Combine("accounts"));
        var account = store.Add(ProviderAccount.Create(ProviderKind.YandexCalDav, "Yandex", "user@yandex.ru"));

        Assert.True(store.Save(account.Id, new ProviderSecret { Password = "secret" }));
        Assert.True(store.Save(account.Id, new ProviderSecret()));
        Assert.False(store.TryGet(account.Id, out _));

        Assert.True(store.Save(account.Id, new ProviderSecret { AccessToken = "token" }));
        Assert.True(((IProviderSecretStore)store).Remove(account.Id));
        Assert.False(store.TryGet(account.Id, out _));
        Assert.NotNull(store.Find(account.Id));
    }

    [Fact]
    public void RemoveAccount_RemovesTheSecretBeforeTheMetadata()
    {
        using var root = new ProviderTestRoot();
        var store = new DpapiAccountStore(root.Combine("accounts"));
        var account = store.Add(ProviderAccount.Create(ProviderKind.Google, "Personal"));
        store.Save(account.Id, new ProviderSecret { RefreshToken = "refresh" });

        Assert.True(store.Remove(account.Id));
        Assert.False(store.TryGet(account.Id, out _));

        // The account is gone, so no orphan metadata can reappear from the secret file either.
        var rawAccounts = File.ReadAllText(Path.Combine(store.Directory, DpapiAccountStore.AccountsFileName));
        Assert.DoesNotContain(account.Id, rawAccounts, StringComparison.Ordinal);
    }

    [Fact]
    public void CorruptAccountsFile_IsQuarantinedBeforeOverwriteAndCanBeRecovered()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        Directory.CreateDirectory(directory);
        var accountsPath = Path.Combine(directory, DpapiAccountStore.AccountsFileName);
        const string corrupt = "{ this is not json";
        File.WriteAllText(accountsPath, corrupt);

        var store = new DpapiAccountStore(directory);

        Assert.Empty(store.List());
        Assert.Null(store.Find("missing"));

        var added = store.Add(ProviderAccount.Create(ProviderKind.IcsSubscription, "Recovered"));
        Assert.Single(store.List());
        Assert.Equal(added.Id, store.List()[0].Id);

        var quarantined = Directory.GetFiles(directory, DpapiAccountStore.AccountsFileName + ".corrupt-*");
        Assert.Single(quarantined);
        Assert.Equal(corrupt, File.ReadAllText(quarantined[0]));
    }

    [Fact]
    public void TruncatedAccountsFile_ReadsAsEmptyAndIsQuarantinedOnWrite()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, DpapiAccountStore.AccountsFileName),
            "{\"version\":2,\"accounts\":[{\"id\":\"abc\"");

        var store = new DpapiAccountStore(directory);
        Assert.Empty(store.List());

        store.Add(ProviderAccount.Create(ProviderKind.IcsSubscription, "Recovered"));
        Assert.Single(Directory.GetFiles(directory, DpapiAccountStore.AccountsFileName + ".corrupt-*"));
    }

    [Fact]
    public void CorruptSecretsFile_IsQuarantinedAndCanBeReplaced()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        Directory.CreateDirectory(directory);
        var secretsPath = Path.Combine(directory, DpapiAccountStore.SecretsFileName);
        byte[] corrupt = [1, 2, 3, 4, 5];
        File.WriteAllBytes(secretsPath, corrupt);

        var store = new DpapiAccountStore(directory);

        Assert.False(store.TryGet("account", out _));
        Assert.True(store.Save("account", new ProviderSecret { Password = "new-secret" }));
        Assert.True(store.TryGet("account", out var secret));
        Assert.Equal("new-secret", secret!.Password);

        var quarantined = Directory.GetFiles(directory, DpapiAccountStore.SecretsFileName + ".corrupt-*");
        Assert.Single(quarantined);
        Assert.Equal(corrupt, File.ReadAllBytes(quarantined[0]));
    }

    [Fact]
    public void OneDamagedSecretRecord_DoesNotHideTheOtherAccounts()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        var store = new DpapiAccountStore(directory);
        var first = store.Add(ProviderAccount.Create(ProviderKind.Google, "Personal"));
        var second = store.Add(ProviderAccount.Create(ProviderKind.GenericCalDav, "Nextcloud"));
        store.Save(first.Id, new ProviderSecret { AccessToken = "first-token" });
        store.Save(second.Id, new ProviderSecret { Password = "second-password" });

        var secretsPath = Path.Combine(directory, DpapiAccountStore.SecretsFileName);
        var damaged = false;
        var updatedSecrets = new System.Collections.Generic.Dictionary<string, string>(StringComparer.Ordinal);
        using (var document = JsonDocument.Parse(File.ReadAllText(secretsPath)))
        {
            foreach (var property in document.RootElement.GetProperty("secrets").EnumerateObject())
            {
                var value = property.Value.GetString()!;
                if (property.Name == first.Id)
                {
                    var bytes = Convert.FromBase64String(value);
                    bytes[bytes.Length / 2] ^= 0xFF;
                    value = Convert.ToBase64String(bytes);
                    damaged = true;
                }

                updatedSecrets[property.Name] = value;
            }
        }

        Assert.True(damaged);
        File.WriteAllText(secretsPath, JsonSerializer.Serialize(
            new { version = 2, secrets = updatedSecrets },
            new JsonSerializerOptions { WriteIndented = true }));

        var reloaded = new DpapiAccountStore(directory);
        Assert.False(reloaded.TryGet(first.Id, out _));
        Assert.True(reloaded.TryGet(second.Id, out var secret));
        Assert.Equal("second-password", secret!.Password);

        // The healthy record survives a rewrite; the damaged original is preserved aside.
        Assert.True(reloaded.Save(second.Id, new ProviderSecret { Password = "rotated" }));
        Assert.Single(Directory.GetFiles(directory, DpapiAccountStore.SecretsFileName + ".corrupt-*"));
        Assert.True(reloaded.TryGet(second.Id, out var rotated));
        Assert.Equal("rotated", rotated!.Password);
    }

    [Fact]
    public void HandWrittenTokenizedIcsRemoteId_IsRedactedOnRead()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, DpapiAccountStore.AccountsFileName),
            $$"""
            {
              "version": 2,
              "accounts": [
                {
                  "id": "legacy",
                  "kind": 4,
                  "displayName": "Legacy",
                  "remoteId": "https://example.com/private/{{FeedToken}}/feed.ics",
                  "enabled": true,
                  "createdAt": "2026-11-08T00:00:00+00:00"
                }
              ]
            }
            """);

        var store = new DpapiAccountStore(directory);
        var account = store.Find("legacy");

        Assert.NotNull(account);
        Assert.Null(account!.RemoteId);
        Assert.DoesNotContain(FeedToken, account.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void MissingFiles_ReadAsEmpty()
    {
        using var root = new ProviderTestRoot();
        var store = new DpapiAccountStore(root.Combine("accounts"));

        Assert.Empty(store.List());
        Assert.False(store.TryGet("anything", out _));
        Assert.False(((IProviderSecretStore)store).Remove("anything"));
    }

    [Fact]
    public void DefaultDirectory_IsUnderLocalAppData()
    {
        var directory = DpapiAccountStore.DefaultDirectory;

        Assert.Contains("ItsycalForWindows", directory);
        Assert.EndsWith(DpapiAccountStore.FolderName, directory, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Writes_LeaveNoTemporaryFiles()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        var store = new DpapiAccountStore(directory);
        var account = store.Add(ProviderAccount.Create(ProviderKind.Google, "Personal"));
        store.Save(account.Id, new ProviderSecret { AccessToken = "token" });

        Assert.Empty(Directory.GetFiles(directory, "*.tmp"));
    }

    [Fact]
    public void StoreFilesAndDirectory_HaveAProtectedUserOnlyAcl()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        var store = new DpapiAccountStore(directory);
        var account = store.Add(ProviderAccount.Create(ProviderKind.Google, "Personal"));
        store.Save(account.Id, new ProviderSecret { AccessToken = "token" });

        AssertProtectedAcl(directory, isDirectory: true);
        AssertProtectedAcl(
            Path.Combine(directory, DpapiAccountStore.AccountsFileName),
            isDirectory: false);
        AssertProtectedAcl(
            Path.Combine(directory, DpapiAccountStore.SecretsFileName),
            isDirectory: false);
    }

    [Fact]
    public void TwoStoreInstances_ConcurrentWrites_DoNotLoseAccounts()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        var first = new DpapiAccountStore(directory);
        var second = new DpapiAccountStore(directory);
        var ids = Enumerable.Range(0, 20).Select(_ => Guid.NewGuid().ToString("N")).ToArray();

        Parallel.For(0, ids.Length, index =>
        {
            var store = index % 2 == 0 ? first : second;
            store.Add(ProviderAccount.Create(ProviderKind.Google, "Concurrent", id: ids[index]));
        });

        var listed = new DpapiAccountStore(directory).List();
        Assert.Equal(ids.Length, listed.Count);
        Assert.All(ids, id => Assert.Contains(listed, account => account.Id == id));
    }

    private static int CountSecretRecords(string secretsJson)
    {
        using var document = JsonDocument.Parse(secretsJson);
        return document.RootElement.GetProperty("secrets").EnumerateObject().Count();
    }

    private static void AssertProtectedAcl(string path, bool isDirectory)
    {
        FileSystemSecurity security = isDirectory
            ? new DirectoryInfo(path).GetAccessControl(AccessControlSections.Access)
            : new FileInfo(path).GetAccessControl(AccessControlSections.Access);

        var rules = security
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        var currentUser = WindowsIdentity.GetCurrent().User;

        Assert.True(security.AreAccessRulesProtected, $"{path} must not inherit access rules.");
        Assert.NotNull(currentUser);
        Assert.Contains(rules, rule =>
            rule.IdentityReference == currentUser &&
            rule.AccessControlType == AccessControlType.Allow);
        Assert.DoesNotContain(rules, rule =>
            rule.IdentityReference == new SecurityIdentifier(WellKnownSidType.WorldSid, null));
        Assert.DoesNotContain(rules, rule =>
            rule.IdentityReference == new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));
        Assert.DoesNotContain(rules, rule => rule.IsInherited);
    }
}
