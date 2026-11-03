using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Tests;

public class InventoryViewModelTests
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
    public async Task ВведенноеРасхождение_ПоказываетИндикаторИФильтр()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var first = await CreateItemAsync(db, "P-1", "Фильтр масляный");
            var second = await CreateItemAsync(db, "P-2", "Колодки тормозные");
            var operations = new OperationsService(db);
            await operations.AddAsync(OperationType.Receipt, first.Id, 2, null);
            await operations.AddAsync(OperationType.Receipt, second.Id, 4, null);
            var viewModel = new InventoryViewModel(new InventoryService(db, operations));
            await viewModel.ReloadAsync();

            Assert.False(viewModel.HasUnapplied);
            Assert.Equal("Неприменённых расхождений нет", viewModel.UnappliedText);

            var firstRow = Assert.Single(viewModel.Rows, r => r.Code == "P-1");
            firstRow.FactInput = "5";

            Assert.True(viewModel.HasUnapplied);
            Assert.Equal("Неприменённые расхождения: 1", viewModel.UnappliedText);

            viewModel.DiscrepancyFilter = "Есть расхождения";

            var filtered = Assert.Single(viewModel.Rows);
            Assert.Equal("P-1", filtered.Code);
            Assert.Equal(3, filtered.Discrepancy);
            Assert.True(filtered.CanApply);
            Assert.Equal("Приход +3", filtered.ActionLabel);
        }
    }

    [Fact]
    public async Task ФильтрНевведенногоФакта_ПоказываетТолькоНезаполненныеСтроки()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var first = await CreateItemAsync(db, "P-1", "Фильтр масляный");
            var second = await CreateItemAsync(db, "P-2", "Колодки тормозные");
            var operations = new OperationsService(db);
            await operations.AddAsync(OperationType.Receipt, first.Id, 2, null);
            await operations.AddAsync(OperationType.Receipt, second.Id, 4, null);
            var viewModel = new InventoryViewModel(new InventoryService(db, operations));
            await viewModel.ReloadAsync();
            Assert.Single(viewModel.Rows, r => r.Code == "P-1").FactInput = "2";

            viewModel.DiscrepancyFilter = "Факт не введён";

            var row = Assert.Single(viewModel.Rows);
            Assert.Equal("P-2", row.Code);
            Assert.Null(row.Discrepancy);
            Assert.False(row.CanApply);
        }
    }

    [Fact]
    public async Task СбросФильтров_ВосстанавливаетВесьСписок()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            await CreateItemAsync(db, "P-1", "Фильтр масляный");
            await CreateItemAsync(db, "P-2", "Колодки тормозные");
            var viewModel = new InventoryViewModel(new InventoryService(db, new OperationsService(db)));
            await viewModel.ReloadAsync();

            viewModel.Filter = "фильтр";
            viewModel.DiscrepancyFilter = "Факт не введён";

            Assert.Single(viewModel.Rows);
            Assert.True(viewModel.HasActiveFilters);

            viewModel.ClearFiltersCommand.Execute(null);

            Assert.Equal(string.Empty, viewModel.Filter);
            Assert.Equal("Все", viewModel.DiscrepancyFilter);
            Assert.False(viewModel.HasActiveFilters);
            Assert.Equal(2, viewModel.Rows.Count);
        }
    }
}
