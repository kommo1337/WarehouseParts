using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Tests;

public class OperationDialogViewModelTests
{
    private static async Task<Item> CreateItemAsync(AppDbContext db, string code, string name)
    {
        var item = new Item
        {
            Code = code,
            Name = name,
            Unit = UnitType.Piece
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            if (condition())
            {
                return;
            }
            await Task.Delay(20);
        }

        throw new TimeoutException("Условие не выполнилось в отведённое время.");
    }

    [Fact]
    public async Task ПредустановленнаяПозиция_ПоказываетОстатокДляПрихода()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db, "P-1", "Фильтр масляный");
            var operations = new OperationsService(db);
            await operations.AddAsync(OperationType.Receipt, item.Id, 5, null);
            var viewModel = new OperationDialogViewModel(operations, new CatalogService(db, operations), OperationType.Receipt, item);

            await WaitUntilAsync(() => !viewModel.IsLoadingChoices && viewModel.AvailableStockText.Length > 0);

            Assert.True(viewModel.HasPresetItem);
            Assert.True(viewModel.HasSelectedItem);
            Assert.False(viewModel.IsIssue);
            Assert.Equal("Новый приход", viewModel.TitleText);
            Assert.Equal("Остаток: 5 Шт", viewModel.AvailableStockText);
        }
    }

    [Fact]
    public async Task РасходБольшеОстатка_ПоказываетОшибкуКоличестваИЗапрашиваетФокус()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db, "P-1", "Фильтр масляный");
            var operations = new OperationsService(db);
            await operations.AddAsync(OperationType.Receipt, item.Id, 2, null);
            var viewModel = new OperationDialogViewModel(operations, new CatalogService(db, operations), OperationType.Issue, item);
            var focusRequests = new List<string>();
            viewModel.FocusRequested += focusRequests.Add;
            await WaitUntilAsync(() => !viewModel.IsLoadingChoices && viewModel.HasSelectedItem);

            viewModel.QuantityText = "3";
            var (ok, error, field) = await viewModel.SaveAsync();

            Assert.False(ok);
            Assert.Equal("QuantityBox", field);
            Assert.Contains("Недостаточно остатка", error);
            Assert.Contains("Недостаточно остатка", viewModel.QuantityError);
            Assert.Contains("QuantityBox", focusRequests);
            Assert.False(viewModel.IsSaving);
        }
    }

    [Fact]
    public async Task КорректноеСохранение_СоздаётОперациюИСообщениеПодтверждения()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db, "P-1", "Фильтр масляный");
            var operations = new OperationsService(db);
            var viewModel = new OperationDialogViewModel(operations, new CatalogService(db, operations), OperationType.Receipt, item);
            await WaitUntilAsync(() => !viewModel.IsLoadingChoices && viewModel.HasSelectedItem);

            viewModel.QuantityText = "4";
            viewModel.Comment = " Поставка ";
            var (ok, error, _) = await viewModel.SaveAsync();

            Assert.True(ok);
            Assert.Null(error);
            Assert.Equal(4, await operations.GetStockAsync(item.Id, CancellationToken.None));
            Assert.Equal("Приход 4 Шт по позиции «P-1 — Фильтр масляный» сохранён", viewModel.SavedMessage);
            Assert.False(viewModel.IsSaving);
        }
    }

    [Fact]
    public async Task ИзменениеПоисковогоТекста_ОчищаетУстаревшийВыбор()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db, "P-1", "Фильтр масляный");
            var operations = new OperationsService(db);
            var viewModel = new OperationDialogViewModel(operations, new CatalogService(db, operations), OperationType.Receipt, item);
            await WaitUntilAsync(() => !viewModel.IsLoadingChoices && viewModel.HasSelectedItem);

            viewModel.FilterText = "отсутствующая запчасть";

            Assert.False(viewModel.HasSelectedItem);
            Assert.Equal(string.Empty, viewModel.AvailableStockText);
            await WaitUntilAsync(() => !viewModel.IsLoadingChoices && viewModel.ItemChoices.Count == 0);
            Assert.Equal("Подходящие позиции не найдены", viewModel.ChoicesStatusText);
        }
    }

    [Fact]
    public async Task EnterПодставляетПервоеСовпадение()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            await CreateItemAsync(db, "P-1", "Фильтр масляный");
            var operations = new OperationsService(db);
            var viewModel = new OperationDialogViewModel(operations, new CatalogService(db, operations), OperationType.Receipt, null);

            viewModel.FilterText = "фильтр";
            await WaitUntilAsync(() => !viewModel.IsLoadingChoices && viewModel.ItemChoices.Count > 0);

            viewModel.PickTopMatch();

            Assert.NotNull(viewModel.SelectedChoice);
            Assert.Equal(viewModel.SelectedChoice.DisplayText, viewModel.FilterText);
            Assert.True(viewModel.HasSelectedItem);
        }
    }
}
