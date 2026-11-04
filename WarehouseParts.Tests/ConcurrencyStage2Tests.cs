using System.Globalization;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.Ui;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Tests;

public class ConcurrencyStage2Tests
{
    [Fact]
    public async Task МассовыйПоиск_ОтмененныйТокен_ПрерываетПоиск()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var search = new SearchService(db, new OperationsService(db));
            using var cts = new CancellationTokenSource();
            cts.Cancel();

            await Assert.ThrowsAnyAsync<OperationCanceledException>(
                () => search.SearchManyAsync(new[] { "123", "456" }, cts.Token));
        }
    }

    [Fact]
    public async Task МассовыйПоиск_ОтменаПриУходе_НеПрименяетРезультат()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            for (var i = 0; i < 30; i++)
            {
                db.Items.Add(new Item { Code = $"B{i:D3}", Name = $"Деталь {i}", Unit = UnitType.Piece });
            }

            await db.SaveChangesAsync();

            var search = new SearchService(db, new OperationsService(db));
            var viewModel = new SearchViewModel(search);
            viewModel.BulkInputText = string.Join(", ", Enumerable.Range(0, 300).Select(i => $"NUM{i:D4}"));
            viewModel.MassSearchStarted = () =>
            {
                viewModel.CancelPending();
                return Task.CompletedTask;
            };

            await viewModel.SearchAllNumbersCommand.ExecuteAsync(null);

            Assert.False(viewModel.ShowMassResult);
        }
    }

    [Fact]
    public async Task МассовыйПоиск_НаходитНомераИСтроитСводку()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = new Item { Code = "MS-1", Name = "Позиция", Unit = UnitType.Piece };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            await db.PartNumbers.AddAsync(new PartNumber
            {
                ItemId = item.Id,
                NumberType = NumberType.Oem,
                NumberRaw = "7700-1234",
                NumberNormalized = NumberNormalizer.Normalize("7700-1234")
            });
            await db.SaveChangesAsync();

            var search = new SearchService(db, new OperationsService(db));
            var viewModel = new SearchViewModel(search);
            viewModel.BulkInputText = "7700-1234, 9999-0000";

            await viewModel.SearchAllNumbersCommand.ExecuteAsync(null);

            Assert.True(viewModel.ShowMassResult);
            Assert.Single(viewModel.MassSummaryLines);
            Assert.Contains("7700-1234", viewModel.MassSummaryLines[0]);
            Assert.Contains("9999-0000", viewModel.MassNotFoundText);
        }
    }

    [Fact]
    public async Task Карточка_БыстраяСменаПозиции_ПоказываетДанныеПоследней()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item1 = new Item { Code = "C-1", Name = "Позиция 1", Unit = UnitType.Piece };
            var item2 = new Item { Code = "C-2", Name = "Позиция 2", Unit = UnitType.Piece };
            db.Items.AddRange(item1, item2);
            await db.SaveChangesAsync();
            await db.PartNumbers.AddRangeAsync(
                new PartNumber { ItemId = item1.Id, NumberType = NumberType.Oem, NumberRaw = "NUM-1", NumberNormalized = "NUM1" },
                new PartNumber { ItemId = item2.Id, NumberType = NumberType.Oem, NumberRaw = "NUM-2", NumberNormalized = "NUM2" });
            var operations = new OperationsService(db);
            await operations.AddAsync(OperationType.Receipt, item1.Id, 5, null);
            await operations.AddAsync(OperationType.Receipt, item2.Id, 7, null);
            await db.SaveChangesAsync();

            var catalog = new CatalogService(db, operations);
            var search = new SearchService(db, operations);
            var details = new ItemDetailsViewModel(catalog, search, operations);

            var first = details.LoadAsync(item1, 5);
            var second = details.LoadAsync(item2, 7);
            await Task.WhenAll(first, second);

            Assert.Equal(item2.Id, details.Item!.Id);
            Assert.Equal(7, details.Stock);
            Assert.All(details.Numbers, n => Assert.Equal(item2.Id, n.Number.ItemId));
            Assert.All(details.RecentOperations, o => Assert.Equal(item2.Id, o.ItemId));
        }
    }

    [Fact]
    public async Task Инвентаризация_СбросСессии_ОчищаетФактическиеОстатки()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = new Item { Code = "INV-1", Name = "Позиция", Unit = UnitType.Piece };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            var operations = new OperationsService(db);
            await operations.AddAsync(OperationType.Receipt, item.Id, 10, null);

            var service = new InventoryService(db, operations);
            var rows = await service.GetRowsAsync();
            service.SetFact(item.Id, 3);
            Assert.True(service.HasUnappliedDiscrepancies);

            service.ClearSession();

            Assert.False(service.HasUnappliedDiscrepancies);
            var reloaded = await service.GetRowsAsync();
            Assert.Null(Assert.Single(reloaded).Fact);
        }
    }

    [Fact]
    public async Task Инвентаризация_СбросЭкрана_ОчищаетСтроки()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            db.Items.Add(new Item { Code = "INV-2", Name = "Позиция", Unit = UnitType.Piece });
            await db.SaveChangesAsync();

            var viewModel = new InventoryViewModel(new InventoryService(db, new OperationsService(db)));
            await viewModel.ReloadAsync();
            Assert.NotEmpty(viewModel.Rows);

            viewModel.Reset();

            Assert.Empty(viewModel.Rows);
            Assert.False(viewModel.HasUnapplied);
            Assert.Equal("Позиций: 0", viewModel.CountText);
        }
    }

    [Fact]
    public void ЗнакКоличества_ПриходПлюс_РасходМинус()
    {
        var converter = new OperationQuantityConverter();
        var receipt = new Operation { OperationType = OperationType.Receipt, Quantity = 12 };
        var issue = new Operation { OperationType = OperationType.Issue, Quantity = 3 };

        Assert.Equal("+12", converter.Convert(receipt, typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Equal("−3", converter.Convert(issue, typeof(string), null!, CultureInfo.InvariantCulture));
        Assert.Equal("", converter.Convert("", typeof(string), null!, CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task Журнал_ОстаткиНаСтраницахСовпадаютСПолнойИсторией()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var random = new Random(7);
            var items = new List<Item>();
            for (var i = 0; i < 3; i++)
            {
                var item = new Item { Code = $"J-{i}", Name = $"Позиция {i}", Unit = UnitType.Piece };
                db.Items.Add(item);
                items.Add(item);
            }

            await db.SaveChangesAsync();

            var service = new OperationsService(db);
            var stocks = new Dictionary<int, int>();
            for (var i = 0; i < 150; i++)
            {
                var item = items[i % items.Count];
                var stock = stocks.GetValueOrDefault(item.Id);
                var type = stock == 0 || random.Next(2) == 0 ? OperationType.Receipt : OperationType.Issue;
                var quantity = type == OperationType.Receipt ? random.Next(1, 10) : random.Next(1, stock + 1);
                stocks[item.Id] = stock + (type == OperationType.Receipt ? quantity : -quantity);
                await service.AddAsync(type, item.Id, quantity, "стресс");
            }

            var full = await service.GetJournalRowsAsync();
            const int pageSize = 40;
            var collected = new List<JournalRow>();
            for (var skip = 0; skip < full.Count; skip += pageSize)
            {
                var page = await service.GetJournalPageAsync(null, null, null, null, skip, pageSize);
                Assert.Equal(full.Count, page.Total);
                collected.AddRange(page.Rows);
            }

            Assert.Equal(full.Count, collected.Count);
            foreach (var (expected, actual) in full.Zip(collected))
            {
                Assert.Equal(expected.Operation.Id, actual.Operation.Id);
                Assert.Equal(expected.BalanceAfter, actual.BalanceAfter);
            }
        }
    }
}
