using System.Runtime.InteropServices;
using System.Text.Json;
using Span.Models;
using Span.Services;

namespace Span.Tests.Services;

[TestClass]
public class FavoritesServiceTests
{
    private string _directory = null!;
    private string _storePath = null!;
    private string? _settings;
    private int _settingsWrites;
    private int _imports;

    [TestInitialize]
    public void Initialize()
    {
        _directory = Path.Combine(Path.GetTempPath(), "SpanFavoritesTests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_directory);
        _storePath = Path.Combine(_directory, "favorites.json");
    }

    [TestCleanup]
    public void Cleanup() => Directory.Delete(_directory, recursive: true);

    private FavoritesService CreateService(Func<List<FavoriteItem>>? import = null,
        Func<string?>? readSettings = null, Action<string>? writeSettings = null)
        => new(_storePath, () =>
        {
            _imports++;
            return import?.Invoke() ?? new List<FavoriteItem> { new() { Path = @"C:\Pinned" } };
        }, readSettings ?? (() => _settings), writeSettings ?? (json =>
        {
            _settingsWrites++;
            _settings = json;
        }));

    private static string Serialize(params FavoriteEntry[] entries) => JsonSerializer.Serialize(entries);

    [TestMethod]
    public void FailedImport_DoesNotPersistFallbackEvenOnCleanup_AndRetriesOnNextStart()
    {
        var service = CreateService(() => throw new COMException("Shell temporarily unavailable"));

        var displayed = service.LoadFavorites();
        service.SaveFavorites(displayed); // MainViewModel.Cleanup also saves the displayed list.

        Assert.IsFalse(service.CanSaveFavorites);
        Assert.IsFalse(File.Exists(_storePath));
        Assert.AreEqual(0, _settingsWrites);
        Assert.AreEqual(displayed.Count, service.AddFavorite(@"C:\New", displayed).Count);
        Assert.AreEqual(displayed.Count, service.RemoveFavorite(@"C:\Pinned", displayed).Count);

        var retry = CreateService();
        var imported = retry.LoadFavorites();
        Assert.AreEqual(@"C:\Pinned", imported.Single().Path);
        Assert.IsTrue(retry.CanSaveFavorites);
        Assert.AreEqual(2, _imports);
        Assert.IsTrue(File.Exists(_storePath));
    }

    [TestMethod]
    public void PartialEnumeration_IsNotCommitted_AndCanRetryOnSameInstance()
    {
        bool failSecondItem = true;
        var service = CreateService(() => FavoritesService.ReadQuickAccessPaths(2, i =>
            i == 0 ? @"C:\First" : failSecondItem ? throw new COMException("offline") : @"C:\Second"));

        service.LoadFavorites();
        Assert.IsFalse(service.CanSaveFavorites);
        Assert.IsFalse(File.Exists(_storePath));
        Assert.AreEqual(0, _settingsWrites);

        failSecondItem = false;
        var result = service.LoadFavorites();
        CollectionAssert.AreEqual(new[] { @"C:\First", @"C:\Second" }, result.Select(f => f.Path).ToArray());
        Assert.IsTrue(service.CanSaveFavorites);
    }

    [TestMethod]
    public void SuccessfullyEmptyQuickAccess_InitializesDefaultsOnce()
    {
        var service = CreateService(() => new List<FavoriteItem>());
        var result = service.LoadFavorites();

        Assert.IsTrue(service.CanSaveFavorites);
        Assert.AreEqual(result.Count, JsonSerializer.Deserialize<List<FavoriteEntry>>(File.ReadAllText(_storePath))!.Count);
        var reloaded = CreateService().LoadFavorites();
        Assert.AreEqual(1, _imports);
        CollectionAssert.AreEqual(result.Select(f => f.Path).ToArray(), reloaded.Select(f => f.Path).ToArray());
    }

    [TestMethod]
    public void ExplicitlyEmptySavedList_IsPreservedWithoutImport()
    {
        File.WriteAllText(_storePath, "[]");
        var service = CreateService();

        Assert.AreEqual(0, service.LoadFavorites().Count);
        Assert.IsTrue(service.CanSaveFavorites);
        Assert.AreEqual(0, _imports);
        Assert.AreEqual("[]", File.ReadAllText(_storePath));
    }

    [TestMethod]
    public void ExistingFavorites_PreserveMetadataOrderAndUnavailablePaths()
    {
        File.WriteAllText(_storePath, Serialize(
            new() { Path = @"Z:\Offline\report.txt", Name = "Report", Color = "#123456", IsFile = true },
            new() { Path = @"C:\Second" }));
        var service = CreateService();

        var favorites = service.LoadFavorites();
        service.SaveFavorites(favorites);
        var saved = JsonSerializer.Deserialize<List<FavoriteEntry>>(File.ReadAllText(_storePath))!;

        Assert.AreEqual(0, _imports);
        Assert.AreEqual("Report", favorites[0].CustomName);
        Assert.AreEqual("#123456", favorites[0].CustomColor);
        Assert.IsTrue(favorites[0].IsFile);
        Assert.AreEqual(@"Z:\Offline\report.txt", saved[0].Path);
        Assert.AreEqual("Report", saved[0].Name);
        Assert.AreEqual("#123456", saved[0].Color);
        Assert.IsTrue(saved[0].IsFile);
        Assert.AreEqual(@"C:\Second", saved[1].Path);
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.tmp").Length);
    }

    [DataTestMethod]
    [DataRow("{")]
    [DataRow("null")]
    [DataRow("[null]")]
    [DataRow("[{}]")]
    [DataRow("[{\"Path\":null}]")]
    [DataRow("[{\"Path\":\"C:\\\\Keep\"},{}]")]
    public void InvalidPrimary_IsNeverTreatedAsFirstRunOrOverwritten(string json)
    {
        File.WriteAllText(_storePath, json);
        var service = CreateService();

        service.SaveFavorites(service.LoadFavorites());

        Assert.IsFalse(service.CanSaveFavorites);
        Assert.AreEqual(json, File.ReadAllText(_storePath));
        Assert.AreEqual(0, _imports);
        Assert.AreEqual(0, _settingsWrites);
    }

    [TestMethod]
    public void CorruptPrimary_UsesBackupForDisplayOnly()
    {
        File.WriteAllText(_storePath, "corrupt but valuable recovery data");
        _settings = Serialize(new FavoriteEntry() { Path = @"C:\Backup", Name = "Keep me" });
        var service = CreateService();

        var result = service.LoadFavorites();
        service.SaveFavorites(new List<FavoriteItem>());

        Assert.AreEqual("Keep me", result.Single().CustomName);
        Assert.IsFalse(service.CanSaveFavorites);
        Assert.AreEqual("corrupt but valuable recovery data", File.ReadAllText(_storePath));
        Assert.AreEqual(0, _imports);
        Assert.AreEqual(0, _settingsWrites);
    }

    [TestMethod]
    public void LockedPrimary_IsNotMissing_AndRecoversAfterUnlock()
    {
        var json = Serialize(new FavoriteEntry() { Path = @"C:\Locked" });
        File.WriteAllText(_storePath, json);
        var service = CreateService();
        using (new FileStream(_storePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            service.SaveFavorites(service.LoadFavorites());
            Assert.IsFalse(service.CanSaveFavorites);
            Assert.AreEqual(0, _imports);
            Assert.AreEqual(0, _settingsWrites);
        }

        Assert.AreEqual(json, File.ReadAllText(_storePath));
        Assert.AreEqual(@"C:\Locked", service.LoadFavorites().Single().Path);
        Assert.IsTrue(service.CanSaveFavorites);
    }

    [TestMethod]
    public void ValidBackupWithMissingPrimary_LoadsWithoutImport()
    {
        _settings = Serialize(new FavoriteEntry() { Path = @"C:\Backup", Color = "#123456" });
        var service = CreateService();

        var result = service.LoadFavorites();
        service.SaveFavorites(result);

        Assert.IsTrue(service.CanSaveFavorites);
        Assert.AreEqual(0, _imports);
        Assert.AreEqual(_settings, File.ReadAllText(_storePath));
    }

    [DataTestMethod]
    [DataRow("null")]
    [DataRow("[")]
    [DataRow("[null]")]
    public void InvalidBackup_DoesNotTriggerMigration(string json)
    {
        _settings = json;
        var service = CreateService();

        service.SaveFavorites(service.LoadFavorites());

        Assert.IsFalse(service.CanSaveFavorites);
        Assert.IsFalse(File.Exists(_storePath));
        Assert.AreEqual(json, _settings);
        Assert.AreEqual(0, _imports);
        Assert.AreEqual(0, _settingsWrites);
    }

    [TestMethod]
    public void SettingsReadFailure_DoesNotMeanMissingStore()
    {
        var service = CreateService(readSettings: () => throw new IOException("unavailable"));

        service.SaveFavorites(service.LoadFavorites());

        Assert.IsFalse(service.CanSaveFavorites);
        Assert.IsFalse(File.Exists(_storePath));
        Assert.AreEqual(0, _imports);
        Assert.AreEqual(0, _settingsWrites);
    }

    [TestMethod]
    public void ValidPrimary_DoesNotDependOnSettingsAvailability()
    {
        File.WriteAllText(_storePath, "[]");
        var service = CreateService(readSettings: () => throw new IOException("unavailable"));

        Assert.AreEqual(0, service.LoadFavorites().Count);
        Assert.IsTrue(service.CanSaveFavorites);
        Assert.AreEqual(0, _imports);
    }

    [TestMethod]
    public void SaveBeforeLoad_DoesNotCreateAStore()
    {
        var service = CreateService();

        service.SaveFavorites(new List<FavoriteItem>());

        Assert.IsFalse(File.Exists(_storePath));
        Assert.AreEqual(0, _settingsWrites);
    }

    [TestMethod]
    public void FailedReplacement_PreservesPrimaryAndBackup()
    {
        var json = Serialize(new FavoriteEntry() { Path = @"C:\Keep" });
        File.WriteAllText(_storePath, json);
        _settings = json;
        var service = CreateService();
        service.LoadFavorites();

        using (new FileStream(_storePath, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            service.SaveFavorites(new List<FavoriteItem>());

        Assert.AreEqual(json, File.ReadAllText(_storePath));
        Assert.AreEqual(json, _settings);
        Assert.AreEqual(0, _settingsWrites);
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.tmp").Length);
    }

    [TestMethod]
    public void FailedInitialFileWrite_DoesNotMarkMigrationInSettings()
    {
        var service = CreateService(() =>
        {
            Directory.CreateDirectory(_storePath); // Blocks the commit after the initial read.
            return new List<FavoriteItem> { new() { Path = @"C:\Imported" } };
        });

        service.SaveFavorites(service.LoadFavorites());

        Assert.IsFalse(service.CanSaveFavorites);
        Assert.AreEqual(0, _settingsWrites);
        Assert.AreEqual(0, Directory.GetFiles(_directory, "*.tmp").Length);
        Directory.Delete(_storePath);
        Assert.AreEqual(@"C:\Pinned", CreateService().LoadFavorites().Single().Path);
        Assert.AreEqual(2, _imports);
    }

    [TestMethod]
    public void StoreCreatedDuringImport_IsNeverOverwritten()
    {
        var winner = Serialize(new FavoriteEntry() { Path = @"C:\OtherInstance", Name = "Keep" });
        var service = CreateService(() =>
        {
            File.WriteAllText(_storePath, winner);
            return new List<FavoriteItem> { new() { Path = @"C:\Imported" } };
        });

        service.SaveFavorites(service.LoadFavorites());

        Assert.IsFalse(service.CanSaveFavorites);
        Assert.AreEqual(winner, File.ReadAllText(_storePath));
        Assert.AreEqual(0, _settingsWrites);
        Assert.AreEqual("Keep", CreateService().LoadFavorites().Single().CustomName);
        Assert.AreEqual(1, _imports);
    }

    [TestMethod]
    public void FailedBackupWrite_DoesNotInvalidateDurablePrimary()
    {
        var service = CreateService(writeSettings: _ => throw new IOException("settings full"));

        Assert.AreEqual(@"C:\Pinned", service.LoadFavorites().Single().Path);
        Assert.IsTrue(service.CanSaveFavorites);
        Assert.AreEqual(@"C:\Pinned", CreateService().LoadFavorites().Single().Path);
        Assert.AreEqual(1, _imports);
    }

    [TestMethod]
    public void QuickAccess_PreservesOfflinePathsAndDeduplicatesCanonicalPaths()
    {
        var paths = new[] { @"\\offline-server\share\Folder", @"Z:\Disconnected", @"z:\Disconnected\", @"C:\" };
        var result = FavoritesService.ReadQuickAccessPaths(paths.Length, i => paths[i]);

        CollectionAssert.AreEqual(new[] { paths[0], paths[1], paths[3] }, result.Select(f => f.Path).ToArray());
    }

    [DataTestMethod]
    [DataRow(null)]
    [DataRow("")]
    [DataRow("relative")]
    [DataRow("::{679f85cb-0220-4080-b29b-5540cc05aab6}")]
    public void UnresolvedQuickAccessItem_AbortsWholeImport(string? path)
    {
        var service = CreateService(() => FavoritesService.ReadQuickAccessPaths(2, i => i == 0 ? @"C:\First" : path));

        service.LoadFavorites();

        Assert.IsFalse(service.CanSaveFavorites);
        Assert.IsFalse(File.Exists(_storePath));
        Assert.AreEqual(0, _settingsWrites);
    }
}
