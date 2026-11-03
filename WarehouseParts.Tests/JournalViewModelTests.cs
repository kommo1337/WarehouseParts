using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Tests;

public class JournalViewModelTests
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

    [Fact]
    public async Task ФильтрБезРезультата_ПоказываетПустоеСостояниеИПозволяетСбросить()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db, "P-1", "Фильтр масляный");
            var operations = new OperationsService(db);
            await operations.AddAsync(OperationType.Receipt, item.Id, 2, null);
            var viewModel = new JournalViewModel(operations);
            await viewModel.RefreshAsync();

            viewModel.FilterCode = "отсутствует";
            await viewModel.RefreshAsync();

            Assert.True(viewModel.HasActiveFilters);
            Assert.True(viewModel.IsEmptyResult);
            Assert.Empty(viewModel.Rows);
            Assert.Equal("Найдено: 0", viewModel.PageText);

            await viewModel.ResetFiltersCommand.ExecuteAsync(null);

            Assert.False(viewModel.HasActiveFilters);
            Assert.False(viewModel.IsEmptyResult);
            Assert.Single(viewModel.Rows);
        }
    }

    [Fact]
    public async Task УдалениеОперации_ТребуетВыбораИПодтверждения()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db, "P-1", "Фильтр масляный");
            var operations = new OperationsService(db);
            await operations.AddAsync(OperationType.Receipt, item.Id, 2, null);
            var viewModel = new JournalViewModel(operations);
            await viewModel.RefreshAsync();

            Assert.False(viewModel.CanDelete);
            Assert.False(viewModel.DeleteCommand.CanExecute(null));

            viewModel.SelectedRow = Assert.Single(viewModel.Rows);

            Assert.True(viewModel.CanDelete);
            Assert.True(viewModel.DeleteCommand.CanExecute(null));
            viewModel.ConfirmDelete = _ => false;
            await viewModel.DeleteCommand.ExecuteAsync(null);
            Assert.Single(db.Operations);

            viewModel.ConfirmDelete = message =>
            {
                Assert.Contains("№1", message);
                return true;
            };
            await viewModel.DeleteCommand.ExecuteAsync(null);

            Assert.Empty(db.Operations);
        }
    }

    [Fact]
    public async Task ПериодИТипФильтра_ОтбираютОперацииИСбрасываются()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db, "P-1", "Фильтр масляный");
            var operations = new OperationsService(db);
            await operations.AddAsync(OperationType.Receipt, item.Id, 2, null);
            await operations.AddAsync(OperationType.Issue, item.Id, 1, null);
            var viewModel = new JournalViewModel(operations);
            await viewModel.RefreshAsync();

            viewModel.FilterType = DisplayTexts.Receipt;
            await viewModel.RefreshAsync();

            var receiptRow = Assert.Single(viewModel.Rows);
            Assert.Equal(DisplayTexts.Receipt, receiptRow.OperationTypeDisplay);
            Assert.True(viewModel.HasActiveFilters);

            await viewModel.ResetFiltersCommand.ExecuteAsync(null);

            Assert.False(viewModel.HasActiveFilters);
            Assert.Equal(2, viewModel.Rows.Count);
        }
    }
}
