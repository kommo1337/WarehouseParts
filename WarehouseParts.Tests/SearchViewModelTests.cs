using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Tests;

public class SearchViewModelTests
{
    [Fact]
    public async Task БыстрыйПоиск_ПоказываетПодтверждениеНайденоПоOem()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var search = new SearchService(db, operations);
            var item = new Item { Code = "FL-1233", Name = "Масляный фильтр", Unit = UnitType.Piece };
            db.Items.Add(item);
            await db.SaveChangesAsync();
            await db.PartNumbers.AddAsync(new PartNumber
            {
                ItemId = item.Id,
                NumberType = NumberType.Oem,
                NumberRaw = "17801-F0060",
                NumberNormalized = NumberNormalizer.Normalize("17801-F0060")
            });
            await db.SaveChangesAsync();

            var viewModel = new SearchViewModel(search);
            viewModel.QuickQuery = "17801 f0060";
            viewModel.FindCommand.Execute(null);

            Assert.StartsWith("Найдено по OEM:", viewModel.ConfirmationText);
            Assert.Equal(item.Id, Assert.Single(viewModel.Rows).Item.Id);
        }
    }

    [Fact]
    public async Task ПовторныйПоиск_СохраняетВыбраннуюПозицию()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var search = new SearchService(db, operations);
            foreach (var code in new[] { "FL-1", "FL-2" })
            {
                db.Items.Add(new Item { Code = code, Name = "Фильтр " + code, Unit = UnitType.Piece });
            }

            await db.SaveChangesAsync();

            var viewModel = new SearchViewModel(search);
            viewModel.QuickQuery = "Фильтр";
            viewModel.FindCommand.Execute(null);

            var target = viewModel.Rows.First(r => r.Item.Code == "FL-2");
            viewModel.SelectedRow = target;

            // Повторный поиск (например, после прихода/расхода) не сбрасывает выбор.
            viewModel.FindCommand.Execute(null);

            Assert.NotNull(viewModel.SelectedRow);
            Assert.Equal("FL-2", viewModel.SelectedRow!.Item.Code);
        }
    }
}
