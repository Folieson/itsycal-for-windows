using System;
using System.IO;
using System.Linq;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using ItsycalForWindows.Providers;
using Xunit;

namespace ItsycalForWindows.Tests;

/// <summary>
/// Tests for <see cref="DpapiAccountStore"/>: account CRUD, persistence, DPAPI encryption at rest,
/// corrupt/truncated files, atomic writes and the user-only ACL.
/// </summary>
public sealed class DpapiAccountStoreTests
{
    [Fact]
    public void Add_List_Find_Remove_RoundTripsAccounts()
    {
        using var root = new ProviderTestRoot();
        var store = new DpapiAccountStore(root.Combine("accounts"));

        var first = store.Add(ProviderAccount.Create(ProviderKind.Google, "Personal", "me@example.com", "primary"));
        var second = store.Add(ProviderAccount.Create(ProviderKind.IcsSubscription, "Team feed", remoteId: "https://example.com/team.ics"));

        var list = store.List();
        Assert.Equal(2, list.Count);
        Assert.Contains(list, account => account.Id == first.Id);
        Assert.Contains(list, account => account.Id == second.Id);
        Assert.Equal("Personal", store.Find(first.Id)?.DisplayName);

        Assert.True(store.Remove(first.Id));
        Assert.Null(store.Find(first.Id));
        Assert.Single(store.List());
        Assert.False(store.Remove(first.Id));
    }

    [Fact]
    public void Add_RejectsDuplicateIdAndInvalidKind()
    {
        using var root = new ProviderTestRoot();
        var store = new DpapiAccountStore(root.Combine("accounts"));
        var account = ProviderAccount.Create(ProviderKind.Google, "Work", id: "same-id");
        store.Add(account);

        Assert.Throws<InvalidOperationException>(() => store.Add(account));
        Assert.Throws<ArgumentException>(() =>
            store.Add(ProviderAccount.Create((ProviderKind)999, "Unknown")));
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
    public void Secrets_AreEncryptedAtRestAndRoundTrip()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        var store = new DpapiAccountStore(directory);
        var account = store.Add(ProviderAccount.Create(ProviderKind.Google, "Personal", "me@example.com"));

        const string accessToken = "ya29.SECRET-ACCESS-TOKEN";
        const string refreshToken = "1//SECRET-REFRESH-TOKEN";
        const string password = "SECRET-APP-PASSWORD";
        var secret = new ProviderSecret
        {
            AccessToken = accessToken,
            RefreshToken = refreshToken,
            Password = password,
            ExpiresAt = new DateTimeOffset(2026, 11, 8, 13, 0, 0, TimeSpan.Zero),
        };

        Assert.True(store.Save(account.Id, secret));
        Assert.True(store.TryGet(account.Id, out var loaded));
        Assert.NotNull(loaded);
        Assert.Equal(accessToken, loaded!.AccessToken);
        Assert.Equal(refreshToken, loaded.RefreshToken);
        Assert.Equal(password, loaded.Password);

        var secretsPath = Path.Combine(directory, DpapiAccountStore.SecretsFileName);
        var rawSecrets = Encoding.UTF8.GetString(File.ReadAllBytes(secretsPath));
        Assert.DoesNotContain(accessToken, rawSecrets, StringComparison.Ordinal);
        Assert.DoesNotContain(refreshToken, rawSecrets, StringComparison.Ordinal);
        Assert.DoesNotContain(password, rawSecrets, StringComparison.Ordinal);

        var accountsPath = Path.Combine(directory, DpapiAccountStore.AccountsFileName);
        var rawAccounts = File.ReadAllText(accountsPath);
        Assert.DoesNotContain(accessToken, rawAccounts, StringComparison.Ordinal);
        Assert.DoesNotContain(password, rawAccounts, StringComparison.Ordinal);
        Assert.DoesNotContain("accessToken", rawAccounts, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Secrets_AreSeparateFromTheAccountFile()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        var store = new DpapiAccountStore(directory);
        var account = store.Add(ProviderAccount.Create(ProviderKind.GenericCalDav, "Nextcloud", remoteId: "https://cloud.example.com/dav/"));

        store.Save(account.Id, new ProviderSecret { Password = "secret-password" });

        Assert.True(File.Exists(Path.Combine(directory, DpapiAccountStore.AccountsFileName)));
        Assert.True(File.Exists(Path.Combine(directory, DpapiAccountStore.SecretsFileName)));
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
    public void RemoveAccount_AlsoRemovesItsSecret()
    {
        using var root = new ProviderTestRoot();
        var store = new DpapiAccountStore(root.Combine("accounts"));
        var account = store.Add(ProviderAccount.Create(ProviderKind.Google, "Personal"));
        store.Save(account.Id, new ProviderSecret { RefreshToken = "refresh" });

        Assert.True(store.Remove(account.Id));
        Assert.False(store.TryGet(account.Id, out _));
    }

    [Fact]
    public void CorruptAccountsFile_ReadsAsEmptyAndCanBeRecovered()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, DpapiAccountStore.AccountsFileName), "{ this is not json");

        var store = new DpapiAccountStore(directory);

        Assert.Empty(store.List());
        Assert.Null(store.Find("missing"));

        var added = store.Add(ProviderAccount.Create(ProviderKind.IcsSubscription, "Recovered"));
        Assert.Single(store.List());
        Assert.Equal(added.Id, store.List()[0].Id);
    }

    [Fact]
    public void TruncatedAccountsFile_ReadsAsEmpty()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        Directory.CreateDirectory(directory);
        File.WriteAllText(
            Path.Combine(directory, DpapiAccountStore.AccountsFileName),
            "{\"version\":1,\"accounts\":[{\"id\":\"abc\"");

        var store = new DpapiAccountStore(directory);

        Assert.Empty(store.List());
    }

    [Fact]
    public void CorruptSecretsFile_ReadsAsNoSecretAndCanBeReplaced()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        Directory.CreateDirectory(directory);
        File.WriteAllBytes(Path.Combine(directory, DpapiAccountStore.SecretsFileName), [1, 2, 3, 4, 5]);

        var store = new DpapiAccountStore(directory);

        Assert.False(store.TryGet("account", out _));
        Assert.True(store.Save("account", new ProviderSecret { Password = "new-secret" }));
        Assert.True(store.TryGet("account", out var secret));
        Assert.Equal("new-secret", secret!.Password);
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
    public void AccountsFile_HasAProtectedUserOnlyAcl()
    {
        using var root = new ProviderTestRoot();
        var directory = root.Combine("accounts");
        var store = new DpapiAccountStore(directory);
        var account = store.Add(ProviderAccount.Create(ProviderKind.Google, "Personal"));

        var path = Path.Combine(directory, DpapiAccountStore.AccountsFileName);
        var security = new FileInfo(path).GetAccessControl(AccessControlSections.Access);
        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToArray();
        var currentUser = WindowsIdentity.GetCurrent().User;

        Assert.NotNull(currentUser);
        Assert.Contains(rules, rule =>
            rule.IdentityReference == currentUser &&
            rule.AccessControlType == AccessControlType.Allow);
        Assert.DoesNotContain(rules, rule =>
            rule.IdentityReference == new SecurityIdentifier(WellKnownSidType.WorldSid, null));
        Assert.DoesNotContain(rules, rule =>
            rule.IdentityReference == new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null));
    }
}
