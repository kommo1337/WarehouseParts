using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.Tests;

public class OperationsServiceTests
{
    private static async Task<Item> CreateItemAsync(AppDbContext db, string code = "P001")
    {
        var item = new Item { Code = code, Name = "Фильтр масляный", Unit = UnitType.Piece };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    [Fact]
    public async Task ПриходИРасход_ОстатокКорректен()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db);
            var service = new OperationsService(db);

            await service.AddAsync(OperationType.Receipt, item.Id, 10, null);
            await service.AddAsync(OperationType.Issue, item.Id, 4, null);

            Assert.Equal(6, await service.GetStockAsync(item.Id));
        }
    }

    [Fact]
    public async Task РасходБольшеОстатка_БлокируетсяИНеСохраняется()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db);
            var service = new OperationsService(db);

            await service.AddAsync(OperationType.Receipt, item.Id, 10, null);
            await service.AddAsync(OperationType.Issue, item.Id, 4, null);

            var ex = await Assert.ThrowsAsync<DomainValidationException>(
                () => service.AddAsync(OperationType.Issue, item.Id, 7, null));

            Assert.Equal("Недостаточно остатка: доступно 6", ex.Message);
            Assert.Equal(6, await service.GetStockAsync(item.Id));
        }
    }

    [Fact]
    public async Task РасходРовноПоОстатку_Проводится()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db);
            var service = new OperationsService(db);

            await service.AddAsync(OperationType.Receipt, item.Id, 10, null);
            await service.AddAsync(OperationType.Issue, item.Id, 4, null);
            await service.AddAsync(OperationType.Issue, item.Id, 6, null);

            Assert.Equal(0, await service.GetStockAsync(item.Id));
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task НекорректноеКоличество_Блокируется(int quantity)
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db);
            var service = new OperationsService(db);

            var ex = await Assert.ThrowsAsync<DomainValidationException>(
                () => service.AddAsync(OperationType.Receipt, item.Id, quantity, null));

            Assert.Equal("Количество должно быть целым числом больше нуля", ex.Message);
            Assert.Equal(0, await service.GetStockAsync(item.Id));
        }
    }

    [Fact]
    public async Task НесуществующаяПозиция_Блокируется()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var service = new OperationsService(db);

            var ex = await Assert.ThrowsAsync<DomainValidationException>(
                () => service.AddAsync(OperationType.Receipt, 999, 5, null));

            Assert.Equal("Укажите запчасть", ex.Message);
        }
    }

    [Fact]
    public async Task УдалениеОперации_ОстатокПересчитывается()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db);
            var service = new OperationsService(db);

            await service.AddAsync(OperationType.Receipt, item.Id, 10, null);
            await service.AddAsync(OperationType.Issue, item.Id, 4, null);
            var receipt2 = await service.AddAsync(OperationType.Receipt, item.Id, 2, null);
            Assert.Equal(8, await service.GetStockAsync(item.Id));

            await service.DeleteAsync(receipt2.Id);

            Assert.Equal(6, await service.GetStockAsync(item.Id));
        }
    }

    [Fact]
    public async Task УдалениеЛомающееИсторию_Блокируется()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db);
            var service = new OperationsService(db);

            var receipt10 = await service.AddAsync(OperationType.Receipt, item.Id, 10, null);
            await service.AddAsync(OperationType.Issue, item.Id, 4, null);
            await service.AddAsync(OperationType.Receipt, item.Id, 2, null);

            var ex = await Assert.ThrowsAsync<DomainValidationException>(
                () => service.DeleteAsync(receipt10.Id));

            Assert.Equal(OperationsService.InsufficientStockMessage, ex.Message);
            Assert.Equal(8, await service.GetStockAsync(item.Id));
        }
    }

    [Fact]
    public async Task ОстатокПослеОперации_СчитаетсяКумулятивно()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db);
            var service = new OperationsService(db);

            var receipt10 = await service.AddAsync(OperationType.Receipt, item.Id, 10, null);
            var issue4 = await service.AddAsync(OperationType.Issue, item.Id, 4, null);
            var receipt2 = await service.AddAsync(OperationType.Receipt, item.Id, 2, null);

            Assert.Equal(10, await service.GetBalanceAfterAsync(receipt10.Id));
            Assert.Equal(6, await service.GetBalanceAfterAsync(issue4.Id));
            Assert.Equal(8, await service.GetBalanceAfterAsync(receipt2.Id));
        }
    }

    [Fact]
    public async Task Журнал_ПагинацияИФильтрыСохраняютОстаткиПолнойИстории()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var itemA = await CreateItemAsync(db, "P-A");
            var itemB = await CreateItemAsync(db, "P-B");
            var service = new OperationsService(db);
            var receipt10 = await service.AddAsync(OperationType.Receipt, itemA.Id, 10, null);
            var issue4 = await service.AddAsync(OperationType.Issue, itemA.Id, 4, null);
            var receiptB = await service.AddAsync(OperationType.Receipt, itemB.Id, 3, null);
            var receipt2 = await service.AddAsync(OperationType.Receipt, itemA.Id, 2, null);

            var firstPage = await service.GetJournalPageAsync(null, null, null, null, 0, 2);
            Assert.Equal(4, firstPage.Total);
            Assert.Equal(new[] { receipt2.Id, receiptB.Id }, firstPage.Rows.Select(r => r.Id));
            Assert.Equal(new[] { 8, 3 }, firstPage.Rows.Select(r => r.BalanceAfter));

            var secondPage = await service.GetJournalPageAsync(null, null, null, null, 2, 2);
            Assert.Equal(new[] { issue4.Id, receipt10.Id }, secondPage.Rows.Select(r => r.Id));
            Assert.Equal(new[] { 6, 10 }, secondPage.Rows.Select(r => r.BalanceAfter));

            var filtered = await service.GetJournalPageAsync("p-a", OperationType.Issue, null, null, 0, 2);
            Assert.Equal(1, filtered.Total);
            var filteredRow = Assert.Single(filtered.Rows);
            Assert.Equal(issue4.Id, filteredRow.Id);
            Assert.Equal(6, filteredRow.BalanceAfter);

            var beyondLastPage = await service.GetJournalPageAsync(null, null, null, null, 20, 2);
            Assert.Equal(4, beyondLastPage.Total);
            Assert.Empty(beyondLastPage.Rows);
        }
    }
}
