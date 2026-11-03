using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Tests;

public class ItemDialogViewModelTests
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
    public async Task НоваяПозиция_СохраняетНомераИНачальныйОстаток()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var catalog = new CatalogService(db, operations);
            var viewModel = new ItemDialogViewModel(catalog, operations, null)
            {
                Code = "P-1",
                Name = "Фильтр масляный",
                StockQuantityText = "7"
            };
            viewModel.NewNumberTypeText = DisplayTexts.Oem;
            viewModel.NewNumberText = "17801-F0060";
            Assert.True(viewModel.AddPendingNumber().Ok);

            var (ok, error, field) = await viewModel.SaveAsync();

            Assert.True(ok);
            Assert.Null(error);
            Assert.Null(field);
            var item = await db.Items.SingleAsync();
            Assert.Equal(7, await operations.GetStockAsync(item.Id, CancellationToken.None));
            Assert.Single(await catalog.GetNumbersAsync(item.Id));
            Assert.Equal(item.Id, viewModel.SavedItemId);
            Assert.False(viewModel.HasChanges);
        }
    }

    [Fact]
    public async Task ПустыеОбязательныеПоля_ПоказываютОшибкуИЗапрашиваютФокус()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var viewModel = new ItemDialogViewModel(
                new CatalogService(db, new OperationsService(db)),
                new OperationsService(db),
                null);
            var focusRequests = new List<string>();
            viewModel.FocusRequested += focusRequests.Add;

            var (ok, error, field) = await viewModel.SaveAsync();

            Assert.False(ok);
            Assert.Equal("Введите номер запчасти", error);
            Assert.Equal("CodeTextBox", field);
            Assert.Equal("Введите номер запчасти", viewModel.CodeError);
            Assert.Contains("CodeTextBox", focusRequests);

            viewModel.Code = "P-1";
            (ok, error, field) = await viewModel.SaveAsync();

            Assert.False(ok);
            Assert.Equal("Введите наименование", error);
            Assert.Equal("NameTextBox", field);
            Assert.Equal("Введите наименование", viewModel.NameError);
            Assert.Contains("NameTextBox", focusRequests);
        }
    }

    [Fact]
    public async Task СкрытыеРазмеры_СохраняютсяПриРедактировании()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db, "D-1", "Диск тормозной");
            item.Diameter = 278m;
            await db.SaveChangesAsync();
            var catalog = new CatalogService(db, new OperationsService(db));
            var editRow = Assert.Single(await catalog.GetRowsAsync());
            var viewModel = new ItemDialogViewModel(catalog, new OperationsService(db), editRow);

            Assert.False(viewModel.HasChanges);
            Assert.True(viewModel.ShowExtendedParams);
            Assert.Equal(278m, decimal.Parse(viewModel.DiameterText));

            viewModel.ShowExtendedParams = false;
            viewModel.Name = "Диск тормозной передний";
            Assert.False(viewModel.ShowExtendedParams);
            var (ok, _, _) = await viewModel.SaveAsync();

            Assert.True(ok);
            var updated = await db.Items.SingleAsync();
            Assert.Equal("Диск тормозной передний", updated.Name);
            Assert.Equal(278m, updated.Diameter);
        }
    }

    [Fact]
    public async Task ДублирующийсяКод_ПоказываетОшибкуВПолеКода()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            await CreateItemAsync(db, "P-1", "Фильтр масляный");
            var viewModel = new ItemDialogViewModel(
                new CatalogService(db, new OperationsService(db)),
                new OperationsService(db),
                null)
            {
                Code = "p-1",
                Name = "Другая запчасть"
            };

            var (ok, error, field) = await viewModel.SaveAsync();

            Assert.False(ok);
            Assert.Equal(CatalogService.DuplicateCodeMessage, error);
            Assert.Equal("CodeTextBox", field);
            Assert.Equal(CatalogService.DuplicateCodeMessage, viewModel.CodeError);
        }
    }

    [Fact]
    public async Task НекорректныйПараметр_ПоказываетОшибкуРядомСПолем()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var viewModel = new ItemDialogViewModel(
                new CatalogService(db, new OperationsService(db)),
                new OperationsService(db),
                null)
            {
                Code = "D-1",
                Name = "Диск тормозной",
                DiameterText = "-2"
            };
            var focusRequests = new List<string>();
            viewModel.FocusRequested += focusRequests.Add;

            var (ok, error, field) = await viewModel.SaveAsync();

            Assert.False(ok);
            Assert.Equal("Диаметр должен быть неотрицательным числом", error);
            Assert.Equal("DiameterText", field);
            Assert.Equal("Диаметр должен быть неотрицательным числом", viewModel.DiameterError);
            Assert.Contains("DiameterText", focusRequests);
        }
    }

    [Fact]
    public void НекорректныйНовыйНомер_ПоказываетОшибкуИОставляетФормуИзменённой()
    {
        using var db = TestDb.CreateContext(out var connection);
        using (connection)
        {
            var viewModel = new ItemDialogViewModel(
                new CatalogService(db, new OperationsService(db)),
                new OperationsService(db),
                null)
            {
                NewNumberText = "---"
            };

            var (ok, error) = viewModel.AddPendingNumber();

            Assert.False(ok);
            Assert.Equal("Введите номер", error);
            Assert.Equal("Введите номер", viewModel.NewNumberError);
            Assert.True(viewModel.HasChanges);
        }
    }

    [Fact]
    public void МассовыйВвод_ЧерезЗапятые_РазбираетСписокИПоказываетИтог()
    {
        using var db = TestDb.CreateContext(out var connection);
        using (connection)
        {
            var viewModel = new ItemDialogViewModel(
                new CatalogService(db, new OperationsService(db)),
                new OperationsService(db),
                null)
            {
                BulkNumberTypeText = DisplayTexts.Oem,
                BulkNumbersText = " 123-ABC , DEF 456\n123-ABC;789"
            };

            Assert.Contains("номеров: 4", viewModel.BulkPreviewText);

            var (added, skipped, error) = viewModel.AddPendingNumberList();

            Assert.Null(error);
            Assert.Equal(3, added);
            Assert.Equal(1, skipped);
            Assert.Equal("Добавлено: 3, пропущено дубликатов: 1", viewModel.BulkFeedbackText);
            Assert.Equal(string.Empty, viewModel.BulkNumbersText);
            Assert.True(viewModel.HasPendingOem);
            Assert.True(viewModel.HasChanges);
        }
    }

    [Fact]
    public async Task Редактирование_ДобавляетТолькоНовыеНомера()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var catalog = new CatalogService(db, operations);
            var item = await CreateItemAsync(db, "P-1", "Фильтр масляный");
            await catalog.AddNumberAsync(item.Id, NumberType.Oem, "111-222", null);
            var editRow = (await catalog.GetRowsAsync()).Single();
            var viewModel = new ItemDialogViewModel(catalog, operations, editRow);
            await viewModel.InitializeAsync();

            Assert.Single(viewModel.PendingNumbers);
            viewModel.BulkNumberTypeText = DisplayTexts.Oem;
            viewModel.BulkNumbersText = "111-222\n333-444";
            viewModel.AddPendingNumberList();
            viewModel.Name = "Фильтр масляный обновлённый";

            var (ok, error, _) = await viewModel.SaveAsync();

            Assert.True(ok);
            Assert.Null(error);
            var numbers = await catalog.GetNumbersAsync(item.Id);
            Assert.Equal(2, numbers.Count);
            Assert.Contains(numbers, n => n.NumberRaw == "333-444");
        }
    }

    [Fact]
    public async Task ДобавлениеНомера_СохраняетИсходноеНаписание()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var catalog = new CatalogService(db, new OperationsService(db));
            var item = await CreateItemAsync(db, "P-1", "Фильтр масляный");

            var number = await catalog.AddNumberAsync(item.Id, NumberType.Oem, "06a-115 561b", null);

            // Исходное написание сохраняется для отображения и копирования.
            Assert.Equal("06a-115 561b", number.NumberRaw);
            // Нормализованный ключ — для поиска и контроля дубликатов.
            Assert.Equal("06A115561B", number.NumberNormalized);
        }
    }

    [Fact]
    public async Task ПредзаполнениеСпискомOem_ПомещаетНомераВМассовоеПоле()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var viewModel = new ItemDialogViewModel(
                new CatalogService(db, new OperationsService(db)),
                new OperationsService(db),
                null,
                new[] { "123-ABC", "DEF 456" });

            // Номера доступны в массовом поле как исходный текст до подтверждения.
            Assert.Contains("123-ABC", viewModel.BulkNumbersText);
            Assert.Contains("DEF 456", viewModel.BulkNumbersText);
            Assert.Equal(DisplayTexts.Oem, viewModel.BulkNumberTypeText);
            Assert.Empty(viewModel.PendingNumbers);
            // Посев из поиска не считается несохранённым изменением.
            Assert.False(viewModel.HasChanges);

            var (added, _, _) = viewModel.AddPendingNumberList();

            Assert.Equal(2, added);
            Assert.True(viewModel.HasPendingOem);
        }
    }

    [Fact]
    public async Task ПозицияБезOem_СохраняетсяПослеМягкогоПодтверждения()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var viewModel = new ItemDialogViewModel(
                new CatalogService(db, new OperationsService(db)),
                new OperationsService(db),
                null)
            {
                Code = "NO-OEM-1",
                Name = "Позиция без номеров"
            };

            var (ok, error, _) = await viewModel.SaveAsync();

            Assert.True(ok);
            Assert.Null(error);
            Assert.NotNull(viewModel.SavedItemId);
        }
    }
}
