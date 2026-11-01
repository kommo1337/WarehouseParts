using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.Tests;

public class InventoryServiceTests
{
    /// <summary>Создает позицию с учетным остатком 6 (приход 10, расход 4).</summary>
    private static async Task<Item> CreateItemWithStock6Async(AppDbContext db, OperationsService operations)
    {
        var item = new Item { Code = "P001", Name = "Фильтр масляный", Unit = UnitType.Piece };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        await operations.AddAsync(OperationType.Receipt, item.Id, 10, null);
        await operations.AddAsync(OperationType.Issue, item.Id, 4, null);
        return item;
    }

    [Fact]
    public async Task ПустойФактическийОстаток_РасхождениеПустое_КнопкаНедоступна()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var item = await CreateItemWithStock6Async(db, operations);
            var service = new InventoryService(db, operations);

            var row = Assert.Single(await service.GetRowsAsync());

            Assert.Equal(item.Id, row.ItemId);
            Assert.Equal(6, row.Stock);
            Assert.Null(row.Fact);
            Assert.Null(row.Discrepancy);
            Assert.Equal(InventoryAction.None, row.Action);
            Assert.Equal("—", row.ActionLabel);
            Assert.False(service.HasUnappliedDiscrepancies);
        }
    }

    [Fact]
    public async Task ФактБольшеУчетного_Приход()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var item = await CreateItemWithStock6Async(db, operations);
            var service = new InventoryService(db, operations);
            var rows = await service.GetRowsAsync();

            service.SetFact(item.Id, 8);

            var row = Assert.Single(rows);
            Assert.Equal(2, row.Discrepancy);
            Assert.Equal(InventoryAction.Receipt, row.Action);
            Assert.Equal("Приход +2", row.ActionLabel);
            Assert.True(service.HasUnappliedDiscrepancies);
        }
    }

    [Fact]
    public async Task ФактМеньшеУчетного_Расход()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var item = await CreateItemWithStock6Async(db, operations);
            var service = new InventoryService(db, operations);
            var rows = await service.GetRowsAsync();

            service.SetFact(item.Id, 4);

            var row = Assert.Single(rows);
            Assert.Equal(-2, row.Discrepancy);
            Assert.Equal(InventoryAction.Issue, row.Action);
            Assert.Equal("Расход −2", row.ActionLabel);
        }
    }

    [Fact]
    public async Task ФактРавенУчетному_БезИзменений()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var item = await CreateItemWithStock6Async(db, operations);
            var service = new InventoryService(db, operations);
            var rows = await service.GetRowsAsync();

            service.SetFact(item.Id, 6);

            var row = Assert.Single(rows);
            Assert.Equal(0, row.Discrepancy);
            Assert.Equal(InventoryAction.NoChange, row.Action);
            Assert.Equal("Без изменений", row.ActionLabel);
            Assert.False(service.HasUnappliedDiscrepancies);
        }
    }

    [Fact]
    public async Task ФактическийНоль_ЭтоНедостачаАНеПустоеЗначение()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var item = await CreateItemWithStock6Async(db, operations);
            var service = new InventoryService(db, operations);
            var rows = await service.GetRowsAsync();

            service.SetFact(item.Id, 0);

            var row = Assert.Single(rows);
            Assert.NotNull(row.Fact);
            Assert.Equal(-6, row.Discrepancy);
            Assert.Equal(InventoryAction.Issue, row.Action);
            Assert.Equal("Расход −6", row.ActionLabel);
        }
    }

    [Fact]
    public async Task БезПрименения_ЖурналНеМеняется()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var item = await CreateItemWithStock6Async(db, operations);
            var service = new InventoryService(db, operations);
            var rows = await service.GetRowsAsync();
            var operationsBefore = await db.Operations.CountAsync();

            service.SetFact(item.Id, 8);

            Assert.Equal(operationsBefore, await db.Operations.CountAsync());
            Assert.Equal(6, await operations.GetStockAsync(item.Id));
        }
    }

    [Fact]
    public async Task ПрименениеПоложительногоРасхождения_СоздаетПриход()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var item = await CreateItemWithStock6Async(db, operations);
            var service = new InventoryService(db, operations);
            var rows = await service.GetRowsAsync();
            service.SetFact(item.Id, 8);

            var operation = await service.ApplyAsync(item.Id, new DateTime(2026, 11, 1));

            Assert.Equal(OperationType.Receipt, operation.OperationType);
            Assert.Equal(2, operation.Quantity);
            Assert.Equal("Инвентаризация 01.11.2026", operation.Comment);
            Assert.Equal(8, await operations.GetStockAsync(item.Id));

            var row = Assert.Single(rows);
            Assert.Equal(8, row.Stock);
            Assert.Equal(0, row.Discrepancy);
            Assert.Equal(InventoryAction.NoChange, row.Action);
            Assert.False(service.HasUnappliedDiscrepancies);
        }
    }

    [Fact]
    public async Task ПрименениеОтрицательногоРасхождения_СоздаетРасход()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var item = await CreateItemWithStock6Async(db, operations);
            var service = new InventoryService(db, operations);
            var rows = await service.GetRowsAsync();
            service.SetFact(item.Id, 3);

            var operation = await service.ApplyAsync(item.Id, new DateTime(2026, 11, 1));

            Assert.Equal(OperationType.Issue, operation.OperationType);
            Assert.Equal(3, operation.Quantity);
            Assert.Equal("Инвентаризация 01.11.2026", operation.Comment);
            Assert.Equal(3, await operations.GetStockAsync(item.Id));

            var row = Assert.Single(rows);
            Assert.Equal(3, row.Stock);
            Assert.Equal(0, row.Discrepancy);
            Assert.Equal(InventoryAction.NoChange, row.Action);
        }
    }

    [Fact]
    public async Task ПрименениеБезФактаИлиНулевогоРасхождения_Блокируется()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var item = await CreateItemWithStock6Async(db, operations);
            var service = new InventoryService(db, operations);
            var rows = await service.GetRowsAsync();

            var emptyFact = await Assert.ThrowsAsync<DomainValidationException>(
                () => service.ApplyAsync(item.Id, DateTime.Today));
            Assert.Equal("Фактический остаток не заполнен", emptyFact.Message);

            service.SetFact(item.Id, 6);
            var zeroDiscrepancy = await Assert.ThrowsAsync<DomainValidationException>(
                () => service.ApplyAsync(item.Id, DateTime.Today));
            Assert.Equal("Расхождение равно нулю, корректировка не требуется", zeroDiscrepancy.Message);

            Assert.Equal(2, await db.Operations.CountAsync());
        }
    }

    [Fact]
    public async Task ОтрицательныйФактическийОстаток_Блокируется()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var item = await CreateItemWithStock6Async(db, operations);
            var service = new InventoryService(db, operations);
            var rows = await service.GetRowsAsync();

            var ex = Assert.Throws<DomainValidationException>(() => service.SetFact(item.Id, -1));

            Assert.Equal("Фактический остаток не может быть отрицательным", ex.Message);
            Assert.Null(Assert.Single(rows).Fact);
        }
    }

    [Fact]
    public async Task ФактическиеОстатки_НеСохраняютсяМеждуЗапусками()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var item = await CreateItemWithStock6Async(db, operations);

            var firstSession = new InventoryService(db, operations);
            var firstRows = await firstSession.GetRowsAsync();
            firstSession.SetFact(item.Id, 8);
            Assert.NotNull(Assert.Single(firstRows).Fact);

            // Новый запуск приложения: новый экземпляр сервиса над той же базой.
            var secondSession = new InventoryService(db, operations);
            var secondRows = await secondSession.GetRowsAsync();

            Assert.Null(Assert.Single(secondRows).Fact);
        }
    }
}
