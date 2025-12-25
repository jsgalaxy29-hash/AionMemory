using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Maui.Storage;

namespace Aion.AppHost.Services;

public sealed class FirstRunState
{
    private const string CompletedKey = "aion.setup.completed";
    private const string DatabasePathKey = "aion.setup.database_path";
    private const string ProfileNameKey = "aion.setup.profile_name";
    private const string DatabaseKeyStorageKey = "aion_db_key";
    private const string StorageKeyStorageKey = "aion_storage_key";

    public bool IsCompleted => Preferences.Default.Get(CompletedKey, false);

    public string GetDatabasePath(string defaultPath)
        => GetDatabasePathOrDefault(defaultPath);

    public string GetProfileName(string defaultName)
    {
        var stored = Preferences.Default.Get(ProfileNameKey, string.Empty);
        return string.IsNullOrWhiteSpace(stored) ? defaultName : stored;
    }

    public static string GetDatabasePathOrDefault(string defaultPath)
    {
        var stored = Preferences.Default.Get(DatabasePathKey, string.Empty);
        return string.IsNullOrWhiteSpace(stored) ? defaultPath : stored;
    }

    public async Task SaveSetupAsync(string databasePath, string encryptionKey, string profileName)
    {
        var normalizedPath = Path.GetFullPath(databasePath);
        Preferences.Default.Set(DatabasePathKey, normalizedPath);
        Preferences.Default.Set(ProfileNameKey, profileName.Trim());

        await SecureStorage.Default.SetAsync(DatabaseKeyStorageKey, encryptionKey).ConfigureAwait(false);
        await SecureStorage.Default.SetAsync(StorageKeyStorageKey, encryptionKey).ConfigureAwait(false);
    }

    public void MarkCompleted() => Preferences.Default.Set(CompletedKey, true);
}
