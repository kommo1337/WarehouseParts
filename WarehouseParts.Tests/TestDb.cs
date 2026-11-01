using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;

namespace WarehouseParts.Tests;

/// <summary>
/// Изолированная база в памяти для каждого теста.
/// </summary>
public static class TestDb
{
    public static AppDbContext CreateContext(out SqliteConnection connection)
    {
        connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(connection)
            .Options;

        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
