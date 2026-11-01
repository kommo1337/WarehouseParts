using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.Tests;

public class BackupServiceTests
{
    private sealed class TempDatabase : IAsyncDisposable
    {
        public AppDbContext Db { get; }
        public SqliteConnection Connection { get; }
        public string DirectoryPath { get; }
        public string DatabasePath { get; }
        public string BackupDirectory { get; }

        private TempDatabase(AppDbContext db, SqliteConnection connection, string directoryPath, string databasePath, string backupDirectory)
        {
            Db = db;
            Connection = connection;
            DirectoryPath = directoryPath;
            DatabasePath = databasePath;
            BackupDirectory = backupDirectory;
        }

        public static TempDatabase Create()
        {
            var directoryPath = Path.Combine(Path.GetTempPath(), $"wp_backup_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(directoryPath);
            var databasePath = Path.Combine(directoryPath, "sklad.db");
            var connection = new SqliteConnection($"Data Source={databasePath};Pooling=false");
            connection.Open();
            var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options;
            var db = new AppDbContext(options);
            db.Database.EnsureCreated();
            return new TempDatabase(db, connection, directoryPath, databasePath, Path.Combine(directoryPath, "Backups"));
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            await Connection.DisposeAsync();
            Directory.Delete(DirectoryPath, recursive: true);
        }
    }

    [Fact]
    public async Task СозданиеКопии_СоздаетВалидныйФайлБазы()
    {
        await using var temp = TempDatabase.Create();
        temp.Db.Items.Add(new Item { Code = "P001", Name = "Фильтр масляный", Unit = UnitType.Piece });
        await temp.Db.SaveChangesAsync();
        var service = new BackupService(temp.Db, temp.DatabasePath, temp.BackupDirectory);

        var backupPath = await service.CreateBackupAsync();

        Assert.True(File.Exists(backupPath));
        Assert.StartsWith("sklad_", Path.GetFileName(backupPath));
        Assert.EndsWith(".db", backupPath);

        await using var checkConnection = new SqliteConnection($"Data Source={backupPath};Pooling=false");
        await checkConnection.OpenAsync();
        await using var command = checkConnection.CreateCommand();
        command.CommandText = "PRAGMA integrity_check;";
        Assert.Equal("ok", await command.ExecuteScalarAsync());

        var options = new DbContextOptionsBuilder<AppDbContext>().UseSqlite($"Data Source={backupPath};Pooling=false").Options;
        await using var backupDb = new AppDbContext(options);
        Assert.Equal(1, await backupDb.Items.CountAsync());
    }

    [Fact]
    public async Task ОбрезкаСтарыхКопий_ОставляетДесятьПоследних()
    {
        await using var temp = TempDatabase.Create();
        Directory.CreateDirectory(temp.BackupDirectory);
        var now = DateTime.Now;
        for (var i = 0; i < 12; i++)
        {
            var path = Path.Combine(temp.BackupDirectory, $"sklad_{now.AddHours(-i):yyyyMMdd_HHmmss}.db");
            await File.WriteAllTextAsync(path, "dummy");
            File.SetLastWriteTime(path, now.AddHours(-i));
        }

        var service = new BackupService(temp.Db, temp.DatabasePath, temp.BackupDirectory);
        service.PruneOldBackups();

        var remaining = Directory.GetFiles(temp.BackupDirectory);
        Assert.Equal(BackupService.MaxBackupCount, remaining.Length);
        Assert.DoesNotContain(remaining, f => f.Contains(now.AddHours(-11).ToString("yyyyMMdd_HHmmss")));
    }

    [Fact]
    public async Task АвтоматическаяКопия_СоздаетсяРазВСутки()
    {
        await using var temp = TempDatabase.Create();
        var service = new BackupService(temp.Db, temp.DatabasePath, temp.BackupDirectory);

        await service.AutoBackupIfNeededAsync();
        var first = service.GetBackupFiles();
        Assert.Single(first);

        await service.AutoBackupIfNeededAsync();
        Assert.Single(service.GetBackupFiles());
    }
}
