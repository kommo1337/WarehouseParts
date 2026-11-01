using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;

namespace WarehouseParts.Services;

/// <summary>
/// Подготовка базы при запуске приложения:
/// проверка целостности, применение миграций, включение WAL.
/// </summary>
public static class DatabaseInitializer
{
    public static async Task InitializeAsync(AppDbContext db, CancellationToken cancellationToken = default)
    {
        var integrityResult = await ExecutePragmaScalarAsync(db, "PRAGMA integrity_check;", cancellationToken);
        if (!string.Equals(integrityResult, "ok", StringComparison.OrdinalIgnoreCase))
        {
            throw new DatabaseCorruptedException(integrityResult);
        }

        await db.Database.MigrateAsync(cancellationToken);

        await ExecutePragmaScalarAsync(db, "PRAGMA journal_mode=WAL;", cancellationToken);
    }

    private static async Task<string> ExecutePragmaScalarAsync(AppDbContext db, string sql, CancellationToken cancellationToken)
    {
        var connection = db.Database.GetDbConnection();
        var wasOpen = connection.State == System.Data.ConnectionState.Open;
        if (!wasOpen)
        {
            await db.Database.OpenConnectionAsync(cancellationToken);
        }

        try
        {
            await using var command = connection.CreateCommand();
            command.CommandText = sql;
            var result = await command.ExecuteScalarAsync(cancellationToken);
            return result?.ToString() ?? "";
        }
        finally
        {
            if (!wasOpen)
            {
                await db.Database.CloseConnectionAsync();
            }
        }
    }
}
