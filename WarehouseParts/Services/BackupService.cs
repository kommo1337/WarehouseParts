using System.IO;
using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;

namespace WarehouseParts.Services;

/// <summary>
/// Резервное копирование базы: консистентная копия командой SQLite VACUUM INTO
/// в подпапку Backups, не более 10 последних копий.
/// </summary>
public class BackupService
{
    public const int MaxBackupCount = 10;
    private static readonly TimeSpan AutoBackupInterval = TimeSpan.FromHours(24);

    private readonly AppDbContext _db;
    private readonly string _databasePath;
    private readonly string _backupDirectory;

    public BackupService(AppDbContext db, string databasePath, string backupDirectory)
    {
        _db = db;
        _databasePath = databasePath;
        _backupDirectory = backupDirectory;
    }

    public string DatabasePath => _databasePath;

    public string BackupDirectory => _backupDirectory;

    /// <summary>Создает резервную копию sklad_yyyyMMdd_HHmmss.db и удаляет лишние старые копии.</summary>
    public async Task<string> CreateBackupAsync(CancellationToken cancellationToken = default)
    {
        Directory.CreateDirectory(_backupDirectory);
        var targetPath = Path.Combine(_backupDirectory, $"sklad_{DateTime.Now:yyyyMMdd_HHmmss}.db");

        // VACUUM INTO создает консистентную копию всей базы.
        var escaped = targetPath.Replace("'", "''");
        await _db.Database.ExecuteSqlRawAsync("VACUUM INTO '" + escaped + "';", cancellationToken);

        PruneOldBackups();
        return targetPath;
    }

    /// <summary>Автоматическая копия при запуске, если со времени последней копии прошло более 24 часов.</summary>
    public async Task AutoBackupIfNeededAsync(CancellationToken cancellationToken = default)
    {
        var latest = GetLatestBackupTime();
        if (latest is null || DateTime.Now - latest.Value > AutoBackupInterval)
        {
            await CreateBackupAsync(cancellationToken);
        }
    }

    /// <summary>Время создания последней копии; null, если копий нет.</summary>
    public DateTime? GetLatestBackupTime()
    {
        var latest = GetBackupFiles().FirstOrDefault();
        return latest is null ? null : File.GetLastWriteTime(latest);
    }

    public IReadOnlyList<string> GetBackupFiles()
    {
        if (!Directory.Exists(_backupDirectory))
        {
            return Array.Empty<string>();
        }

        return Directory.GetFiles(_backupDirectory, "sklad_*.db")
            .OrderByDescending(f => File.GetLastWriteTime(f))
            .ToList();
    }

    /// <summary>Оставляет не более 10 последних копий, старшие удаляет.</summary>
    public void PruneOldBackups()
    {
        foreach (var file in GetBackupFiles().Skip(MaxBackupCount))
        {
            try
            {
                File.Delete(file);
            }
            catch
            {
                // Удаление старой копии не должно ронять приложение.
            }
        }
    }
}
