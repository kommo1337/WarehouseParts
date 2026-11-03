using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Tests;

public class CatalogViewModelTests
{
    private static async Task<Item> CreateItemAsync(
        AppDbContext db,
        string code,
        string name,
        string? manufacturer = null)
    {
        var item = new Item
        {
            Code = code,
            Name = name,
            Unit = UnitType.Piece,
            Manufacturer = manufacturer
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    [Fact]
    public async Task ЛокальныйФильтр_ИщетПоКодуНаименованиюИПроизводителю()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            await CreateItemAsync(db, "P-1", "Фильтр масляный", "Mann");
            await CreateItemAsync(db, "P-2", "Колодки тормозные", "Bosch");
            await CreateItemAsync(db, "P-3", "Фильтр салона", "Mann-Filter");
            var viewModel = new CatalogViewModel(new CatalogService(db, new OperationsService(db)));
            await viewModel.RefreshAsync();

            viewModel.FilterText = "mann";

            Assert.Equal(2, viewModel.Rows.Count);
            Assert.True(viewModel.HasActiveFilter);
            Assert.Equal("Показано: 2 из 3", viewModel.CountText);
            Assert.All(viewModel.Rows, row => Assert.Contains("Mann", row.Item.Manufacturer, StringComparison.OrdinalIgnoreCase));

            viewModel.ClearFilterCommand.Execute(null);

            Assert.Equal(3, viewModel.Rows.Count);
            Assert.False(viewModel.HasActiveFilter);
            Assert.Equal("Позиций: 3", viewModel.CountText);
        }
    }

    [Fact]
    public async Task ФильтрБезРезультата_ПоказываетОтдельноеСостояние()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            await CreateItemAsync(db, "P-1", "Фильтр масляный");
            var viewModel = new CatalogViewModel(new CatalogService(db, new OperationsService(db)));
            await viewModel.RefreshAsync();

            viewModel.FilterText = "свеча";

            Assert.Empty(viewModel.Rows);
            Assert.True(viewModel.IsFilterEmpty);
            Assert.False(viewModel.IsCatalogEmpty);
            Assert.Equal("По фильтру ничего не найдено", viewModel.EmptyTitle);
        }
    }

    [Fact]
    public async Task КомандыРедактирования_ДоступныТолькоПриВыбореПозицииИНомера()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db, "P-1", "Фильтр масляный");
            db.PartNumbers.Add(new PartNumber
            {
                ItemId = item.Id,
                NumberType = NumberType.Oem,
                NumberRaw = "OEM-1",
                NumberNormalized = NumberNormalizer.Normalize("OEM-1")
            });
            await db.SaveChangesAsync();
            var viewModel = new CatalogViewModel(new CatalogService(db, new OperationsService(db)));
            await viewModel.RefreshAsync();

            Assert.False(viewModel.EditItemCommand.CanExecute(null));
            Assert.False(viewModel.DeleteItemCommand.CanExecute(null));
            Assert.False(viewModel.AddNumberCommand.CanExecute(null));
            Assert.False(viewModel.EditNumberCommand.CanExecute(null));
            Assert.False(viewModel.DeleteNumberCommand.CanExecute(null));

            viewModel.SelectedRow = Assert.Single(viewModel.Rows);

            Assert.True(viewModel.EditItemCommand.CanExecute(null));
            Assert.True(viewModel.DeleteItemCommand.CanExecute(null));
            Assert.True(viewModel.AddNumberCommand.CanExecute(null));
            Assert.False(viewModel.EditNumberCommand.CanExecute(null));

            var numbers = await new CatalogService(db, new OperationsService(db)).GetNumbersAsync(item.Id);
            viewModel.SelectedNumber = new NumberRowViewModel(Assert.Single(numbers));

            Assert.True(viewModel.EditNumberCommand.CanExecute(null));
            Assert.True(viewModel.DeleteNumberCommand.CanExecute(null));
        }
    }

    [Fact]
    public async Task УдалениеПозиции_ТребуетПодтверждениеИПоказываетКод()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            await CreateItemAsync(db, "P-1", "Фильтр масляный");
            var viewModel = new CatalogViewModel(new CatalogService(db, new OperationsService(db)));
            await viewModel.RefreshAsync();
            viewModel.SelectedRow = Assert.Single(viewModel.Rows);
            var confirmations = new List<string>();
            viewModel.ConfirmDelete = message =>
            {
                confirmations.Add(message);
                return false;
            };

            await viewModel.DeleteItemCommand.ExecuteAsync(null);

            Assert.Single(confirmations);
            Assert.Contains("P-1", confirmations[0]);
            Assert.Contains("Фильтр масляный", confirmations[0]);
            Assert.Single(db.Items);

            viewModel.ConfirmDelete = _ => true;
            await viewModel.DeleteItemCommand.ExecuteAsync(null);

            Assert.Empty(db.Items);
            Assert.Empty(viewModel.Rows);
        }
    }

    [Fact]
    public async Task УдалениеНомера_ТребуетПодтверждениеСТипомИЗначением()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db, "P-1", "Фильтр масляный");
            db.PartNumbers.Add(new PartNumber
            {
                ItemId = item.Id,
                NumberType = NumberType.Analog,
                NumberRaw = "AN-1",
                NumberNormalized = NumberNormalizer.Normalize("AN-1")
            });
            await db.SaveChangesAsync();
            var catalog = new CatalogService(db, new OperationsService(db));
            var viewModel = new CatalogViewModel(catalog);
            await viewModel.RefreshAsync();
            viewModel.SelectedRow = Assert.Single(viewModel.Rows);
            viewModel.SelectedNumber = new NumberRowViewModel(Assert.Single(await catalog.GetNumbersAsync(item.Id)));
            var confirmation = string.Empty;
            viewModel.ConfirmDelete = message =>
            {
                confirmation = message;
                return true;
            };

            await viewModel.DeleteNumberCommand.ExecuteAsync(null);

            Assert.Contains("Аналог", confirmation);
            Assert.Contains("AN-1", confirmation);
            Assert.Empty(await catalog.GetNumbersAsync(item.Id));
        }
    }
}
