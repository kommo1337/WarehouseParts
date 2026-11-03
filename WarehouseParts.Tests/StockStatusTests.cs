using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.Tests;

public class StockStatusTests
{
    [Fact]
    public async Task ОстатокНоль_НетВНаличии()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = new Item { Code = "P001", Name = "Фильтр масляный", Unit = UnitType.Piece };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            var service = new OperationsService(db);

            Assert.Equal(StockStatus.OutOfStock, await service.GetStockStatusAsync(item.Id));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(10)]
    public async Task ОстатокБольшеНуля_ВНаличии(int quantity)
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = new Item { Code = "P001", Name = "Фильтр масляный", Unit = UnitType.Piece };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            var service = new OperationsService(db);

            await service.AddAsync(OperationType.Receipt, item.Id, quantity, null);

            Assert.Equal(StockStatus.InStock, await service.GetStockStatusAsync(item.Id));
        }
    }

    [Fact]
    public async Task ОтрицательныйОстаток_ЗаписываетсяВErrorsLog()
    {
        var logPath = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"warehouseparts_test_{Guid.NewGuid():N}.log");
        var originalPath = ErrorLog.Path;
        ErrorLog.Path = logPath;
        try
        {
            await using var db = TestDb.CreateContext(out var connection);
            await using (connection)
            {
                var item = new Item { Code = "P001", Name = "Фильтр масляный", Unit = UnitType.Piece };
                db.Items.Add(item);
                await db.SaveChangesAsync();

                // Обходим проверки сервиса, чтобы смоделировать дефект данных.
                db.Operations.Add(new Operation
                {
                    ItemId = item.Id,
                    OperationType = OperationType.Issue,
                    Quantity = 5,
                    CreatedAt = DateTime.Now
                });
                await db.SaveChangesAsync();

                var service = new OperationsService(db);
                Assert.Equal(-5, await service.GetStockAsync(item.Id));
                _ = await service.GetStocksAsync(new[] { item.Id });

                string? content = null;
                for (var attempt = 0; attempt < 20 && content is null; attempt++)
                {
                    try
                    {
                        content = await File.ReadAllTextAsync(logPath);
                    }
                    catch (IOException) when (attempt < 19)
                    {
                        await Task.Delay(25);
                    }
                }

                Assert.NotNull(content);
                Assert.Contains("Отрицательный остаток", content);
            }
        }
        finally
        {
            ErrorLog.Path = originalPath;
            File.Delete(logPath);
        }
    }
}
