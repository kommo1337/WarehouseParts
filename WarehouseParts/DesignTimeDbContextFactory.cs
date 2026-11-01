using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using WarehouseParts.Data;

namespace WarehouseParts;

/// <summary>
/// Фабрика контекста для инструментов EF Core (создание миграций).
/// В работающем приложении не используется.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite("Data Source=design-time.db")
            .Options;
        return new AppDbContext(options);
    }
}
