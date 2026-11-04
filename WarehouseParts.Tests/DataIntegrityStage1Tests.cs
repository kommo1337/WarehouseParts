using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Tests;

/// <summary>
/// Регрессионные тесты этапа 1: атомарность записи складских данных,
/// идемпотентность сохранения операции, транзакционное удаление,
/// корректный diff номеров карточки и версия нативной SQLite.
/// </summary>
public class DataIntegrityStage1Tests
{
    [Fact]
    public async Task СозданиеПозиции_СохраняетКарточкуНомераИПриходАтомарно()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var catalog = new CatalogService(db, operations);

            var item = await catalog.CreateItemAsync(
                "P-100",
                "Фильтр воздушный",
                UnitType.Piece,
                null,
                null,
                null,
                null,
                null,
                null,
                new[]
                {
                    new PendingNumberInput(NumberType.Oem, "123-ABC", null),
                    new PendingNumberInput(NumberType.Analog, "AF-01", "аналог")
                },
                initialReceiptQuantity: 7,
                initialReceiptComment: "начальный остаток");

            Assert.True(item.Id > 0);
            Assert.Equal(2, (await catalog.GetNumbersAsync(item.Id)).Count);
            Assert.Equal(7, await operations.GetStockAsync(item.Id));
        }
    }

    [Fact]
    public async Task СозданиеПозиции_СбойНаНомереНеОставляетЧастичныхЗаписей()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var catalog = new CatalogService(db, operations);

            await Assert.ThrowsAsync<DomainValidationException>(() =>
                catalog.CreateItemAsync(
                    "P-100",
                    "Фильтр воздушный",
                    UnitType.Piece,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    new[]
                    {
                        new PendingNumberInput(NumberType.Oem, "123-ABC", null),
                        new PendingNumberInput(NumberType.Oem, "-- / ..", null)
                    },
                    initialReceiptQuantity: 7));

            Assert.Empty(db.Items);
            Assert.Empty(db.PartNumbers);
            Assert.Empty(db.Operations);
        }
    }

    [Fact]
    public async Task СозданиеПозиции_СбойНаПриходеНеОставляетЧастичныхЗаписей()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var catalog = new CatalogService(db, operations);

            // Некорректное количество начального прихода роняет весь сценарий.
            await Assert.ThrowsAsync<DomainValidationException>(() =>
                catalog.CreateItemAsync(
                    "P-100",
                    "Фильтр воздушный",
                    UnitType.Piece,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    Array.Empty<PendingNumberInput>(),
                    initialReceiptQuantity: 0));

            Assert.Empty(db.Items);
            Assert.Empty(db.Operations);
        }
    }

    [Fact]
    public async Task СохранениеКарточки_УдалённыйЧерезДиалогНомерПропадаетИзБазы()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var catalog = new CatalogService(db, operations);
            var item = await catalog.AddItemAsync("P-1", "Фильтр масляный", UnitType.Piece, null, null, null);
            await catalog.AddNumberAsync(item.Id, NumberType.Oem, "111-222", null);
            await catalog.AddNumberAsync(item.Id, NumberType.Analog, "333-444", null);
            var editRow = (await catalog.GetRowsAsync()).Single(r => r.Item.Id == item.Id);
            var viewModel = new ItemDialogViewModel(catalog, operations, editRow);
            await viewModel.InitializeAsync();

            viewModel.SelectedPendingNumber = viewModel.PendingNumbers.Single(n => n.NumberRaw == "111-222");
            viewModel.RemovePendingNumber();
            var (ok, error, _) = await viewModel.SaveAsync();

            Assert.True(ok);
            Assert.Null(error);
            var numbers = await catalog.GetNumbersAsync(item.Id);
            Assert.Single(numbers);
            Assert.Equal("333-444", numbers[0].NumberRaw);
        }
    }

    [Fact]
    public async Task СохранениеКарточки_ИзменённыйНомерОбновляетсяПоId()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var catalog = new CatalogService(db, operations);
            var item = await catalog.AddItemAsync("P-1", "Фильтр масляный", UnitType.Piece, null, null, null);
            var number = await catalog.AddNumberAsync(item.Id, NumberType.Oem, "06a-115 561b", null);
            var editRow = (await catalog.GetRowsAsync()).Single(r => r.Item.Id == item.Id);
            var viewModel = new ItemDialogViewModel(catalog, operations, editRow);
            await viewModel.InitializeAsync();

            viewModel.PendingNumbers.Clear();
            viewModel.PendingNumbers.Add(new PendingNumber(number.Id, NumberType.Oem, "06A-115-561B", "обновлён"));
            var (ok, error, _) = await viewModel.SaveAsync();

            Assert.True(ok);
            Assert.Null(error);
            var updated = (await catalog.GetNumbersAsync(item.Id)).Single();
            Assert.Equal("06A-115-561B", updated.NumberRaw);
            Assert.Equal("06A115561B", updated.NumberNormalized);
            Assert.Equal("обновлён", updated.Comment);
        }
    }

    [Fact]
    public async Task СохранениеКарточки_НоваяПозицияСохраняетНомераИНачальныйПриход()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var catalog = new CatalogService(db, operations);
            var viewModel = new ItemDialogViewModel(catalog, operations, null)
            {
                Code = "P-NEW",
                Name = "Колодки",
                StockQuantityText = "5"
            };
            viewModel.BulkNumberTypeText = DisplayTexts.Oem;
            viewModel.BulkNumbersText = "123-ABC";
            viewModel.AddPendingNumberList();

            var (ok, error, _) = await viewModel.SaveAsync();

            Assert.True(ok);
            Assert.Null(error);
            Assert.NotNull(viewModel.SavedItemId);
            var itemId = viewModel.SavedItemId!.Value;
            Assert.Single(await catalog.GetNumbersAsync(itemId));
            Assert.Equal(5, await operations.GetStockAsync(itemId));
        }
    }

    [Fact]
    public async Task ДвойнойSaveAsync_СоздаётРовноОднуОперацию()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var catalog = new CatalogService(db, operations);
            var item = await catalog.AddItemAsync("P-1", "Фильтр масляный", UnitType.Piece, null, null, null);
            var viewModel = new OperationDialogViewModel(operations, catalog, OperationType.Receipt, item)
            {
                QuantityText = "3"
            };

            var first = viewModel.SaveAsync();
            var second = viewModel.SaveAsync();
            var (ok1, _, _) = await first;
            var (ok2, _, _) = await second;

            Assert.True(ok1);
            Assert.False(ok2);
            Assert.Single(db.Operations);
            Assert.Equal(3, await operations.GetStockAsync(item.Id));
        }
    }

    [Fact]
    public async Task ГонкаУдаленияИРасхода_НеДаётОтрицательныйОстаток()
    {
        var path = Path.Combine(Path.GetTempPath(), $"wp-stage1-{Guid.NewGuid():N}.db");
        try
        {
            await using (var seed = CreateFileContext(path))
            {
                var seedOperations = new OperationsService(seed);
                var seedCatalog = new CatalogService(seed, seedOperations);
                var item = await seedCatalog.AddItemAsync("P-1", "Фильтр масляный", UnitType.Piece, null, null, null);
                await seedOperations.AddAsync(OperationType.Receipt, item.Id, 10, null);
                var secondReceipt = await seedOperations.AddAsync(OperationType.Receipt, item.Id, 5, null);
                await seedOperations.AddAsync(OperationType.Issue, item.Id, 3, null);
                var itemId = item.Id;
                var deleteId = secondReceipt.Id;

                var failures = 0;
                var tasks = new List<Task>();
                for (var i = 0; i < 10; i++)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await using var ctx = CreateFileContext(path);
                        var ops = new OperationsService(ctx);
                        try
                        {
                            await ops.AddAsync(OperationType.Issue, itemId, 4, null);
                        }
                        catch (DomainValidationException)
                        {
                            Interlocked.Increment(ref failures);
                        }
                    }));
                    tasks.Add(Task.Run(async () =>
                    {
                        await using var ctx = CreateFileContext(path);
                        var ops = new OperationsService(ctx);
                        try
                        {
                            await ops.DeleteAsync(deleteId);
                        }
                        catch (DomainValidationException)
                        {
                            Interlocked.Increment(ref failures);
                        }
                    }));
                }

                await Task.WhenAll(tasks);

                await using var check = CreateFileContext(path);
                var checkOperations = new OperationsService(check);
                var stock = await checkOperations.GetStockAsync(itemId);
                Assert.True(stock >= 0, $"Остаток стал отрицательным: {stock}");
                var operations = await check.Operations.AsNoTracking().ToListAsync();
                Assert.Equal(stock, operations.Sum(o => o.OperationType == OperationType.Receipt ? o.Quantity : -o.Quantity));
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void НативнаяSQLite_НеНиже_3_50_2()
    {
        using var db = TestDb.CreateContext(out var connection);
        using (connection)
        {
            Assert.True(
                SQLitePCL.raw.sqlite3_libversion_number() >= 3050002,
                $"Ожидалась SQLite 3.50.2 или новее, фактически: {SQLitePCL.raw.sqlite3_libversion().utf8_to_string()}");
        }
    }

    private static AppDbContext CreateFileContext(string path)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={path};Pooling=false")
            .Options;
        var db = new AppDbContext(options);
        db.Database.EnsureCreated();
        return db;
    }
}
