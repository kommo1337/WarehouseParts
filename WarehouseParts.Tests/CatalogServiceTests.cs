using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.Tests;

public class CatalogServiceTests
{
    private static async Task<Item> CreateItemAsync(AppDbContext db, CatalogService service, string code = "P001")
        => await service.AddItemAsync(code, "Фильтр масляный", UnitType.Piece, null, null, null);

    [Fact]
    public async Task ПозицияСОперациями_НеУдаляется()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var operations = new OperationsService(db);
            var service = new CatalogService(db, operations);
            var item = await CreateItemAsync(db, service);
            await operations.AddAsync(OperationType.Receipt, item.Id, 5, null);

            var ex = await Assert.ThrowsAsync<DomainValidationException>(
                () => service.DeleteItemAsync(item.Id));

            Assert.Equal(CatalogService.ItemHasOperationsMessage, ex.Message);
            Assert.NotNull(await db.Items.FindAsync(item.Id));
        }
    }

    [Fact]
    public async Task ПозицияБезОпераций_УдаляетсяВместеСНомерами()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var service = new CatalogService(db, new OperationsService(db));
            var item = await CreateItemAsync(db, service);
            await service.AddNumberAsync(item.Id, NumberType.Oem, "OEM-1", null);

            await service.DeleteItemAsync(item.Id);

            Assert.Empty(await db.Items.ToListAsync());
            Assert.Empty(await db.PartNumbers.ToListAsync());
        }
    }

    [Fact]
    public async Task ДубликатКода_БлокируетсяБезУчетаРегистра()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var service = new CatalogService(db, new OperationsService(db));
            var item = await CreateItemAsync(db, service, "P001");

            var ex = await Assert.ThrowsAsync<DomainValidationException>(
                () => service.AddItemAsync("p001", "Другая запчасть", UnitType.Piece, null, null, null));
            Assert.Equal(CatalogService.DuplicateCodeMessage, ex.Message);

            var second = await service.AddItemAsync("P002", "Другая запчасть", UnitType.Piece, null, null, null);
            var updateEx = await Assert.ThrowsAsync<DomainValidationException>(
                () => service.UpdateItemAsync(second.Id, "P001", "Другая запчасть", UnitType.Piece, null, null, null));
            Assert.Equal(CatalogService.DuplicateCodeMessage, updateEx.Message);
        }
    }

    [Fact]
    public async Task ДубликатКлючаНомераВнутриПозиции_Блокируется_УДругойПозицииРазрешен()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var service = new CatalogService(db, new OperationsService(db));
            var first = await CreateItemAsync(db, service, "P001");
            var second = await CreateItemAsync(db, service, "P002");
            await service.AddNumberAsync(first.Id, NumberType.Oem, "OEM-1", null);

            var ex = await Assert.ThrowsAsync<DomainValidationException>(
                () => service.AddNumberAsync(first.Id, NumberType.Oem, "oem-1", null));
            Assert.Equal(CatalogService.DuplicateNumberMessage, ex.Message);

            // Тот же ключ с другим типом — другой ключ, разрешен.
            await service.AddNumberAsync(first.Id, NumberType.Analog, "OEM-1", null);
            // Тот же ключ у другой позиции — механизм связи, разрешен.
            await service.AddNumberAsync(second.Id, NumberType.Oem, "OEM 1", null);
        }
    }

    [Fact]
    public async Task НекорректныйНомер_Блокируется()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var service = new CatalogService(db, new OperationsService(db));
            var item = await CreateItemAsync(db, service);

            var empty = await Assert.ThrowsAsync<DomainValidationException>(
                () => service.AddNumberAsync(item.Id, NumberType.Oem, "   ", null));
            Assert.Equal("Введите номер", empty.Message);

            var punctuationOnly = await Assert.ThrowsAsync<DomainValidationException>(
                () => service.AddNumberAsync(item.Id, NumberType.Oem, "-- / ..", null));
            Assert.Equal("Номер должен содержать буквы или цифры", punctuationOnly.Message);
        }
    }

    [Fact]
    public async Task МассовыйВвод_ДобавляетНомераИИгнорируетМусор()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var service = new CatalogService(db, new OperationsService(db));
            var item = await CreateItemAsync(db, service);
            var list = string.Join(Environment.NewLine, Enumerable.Range(1, 10).Select(i => $"NUM-{i:D2}"));

            var result = await service.BulkAddNumbersAsync(item.Id, NumberType.Analog, $"  {list}  \r\n\r\n-- // ..\r\n   ");

            Assert.Equal(10, result.Added);
            Assert.Equal(0, result.SkippedDuplicates);
            Assert.Equal(10, await db.PartNumbers.CountAsync());
            Assert.All(await db.PartNumbers.ToListAsync(), n => Assert.Equal(NumberType.Analog, n.NumberType));
        }
    }

    [Fact]
    public async Task МассовыйВводПовторно_ПропускаетДубликаты()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var service = new CatalogService(db, new OperationsService(db));
            var item = await CreateItemAsync(db, service);
            var list = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"NUM-{i:D2}"));
            await service.BulkAddNumbersAsync(item.Id, NumberType.Oem, list);

            var repeat = await service.BulkAddNumbersAsync(item.Id, NumberType.Oem, list);

            Assert.Equal(0, repeat.Added);
            Assert.Equal(10, repeat.SkippedDuplicates);
            Assert.Equal(10, await db.PartNumbers.CountAsync());
        }
    }

    [Fact]
    public async Task МассовыйВвод_ДубликатыВнутриСпискаПропускаются()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var service = new CatalogService(db, new OperationsService(db));
            var item = await CreateItemAsync(db, service);

            var result = await service.BulkAddNumbersAsync(item.Id, NumberType.Oem, "A-1\nA-1\nA1");

            // "A-1" и "A1" после нормализации — один ключ, обе повторные строки пропущены.
            Assert.Equal(1, result.Added);
            Assert.Equal(2, result.SkippedDuplicates);
        }
    }

    [Fact]
    public async Task Автоподбор_ФильтруетПоКодуИНаименованию()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var service = new CatalogService(db, new OperationsService(db));
            await CreateItemAsync(db, service, "FIL-01");
            await service.AddItemAsync("BRK-01", "Колодки тормозные", UnitType.Piece, null, null, null);

            var byCode = await service.SearchItemsAsync("fil");
            var singleByCode = Assert.Single(byCode);
            Assert.Equal("FIL-01", singleByCode.Code);

            var byName = await service.SearchItemsAsync("колод");
            Assert.Equal("BRK-01", Assert.Single(byName).Code);

            Assert.Equal(2, (await service.SearchItemsAsync("")).Count);
            Assert.Empty(await service.SearchItemsAsync("xyz"));
        }
    }

    [Fact]
    public async Task ФильтрыКачества_ОтбираютПозицииСНезаполненнымиДанными()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var service = new CatalogService(db, new OperationsService(db));
            var withOem = await service.AddItemAsync("OEM-01", "С OEM", UnitType.Piece, "Bosch", "VW Golf", null);
            await service.AddNumberAsync(withOem.Id, NumberType.Oem, "12345", null);
            await service.AddItemAsync("NO-OEM", "Без OEM", UnitType.Piece, "Bosch", "VW Golf", null);
            await service.AddItemAsync("NO-MFR", "Без производителя", UnitType.Piece, null, "VW Golf", null);
            await service.AddItemAsync("NO-APP", "Без применимости", UnitType.Piece, "Bosch", null, null);

            var withoutOem = await service.GetRowsAsync(CatalogQualityFilter.WithoutOem);
            Assert.Equal(3, withoutOem.Count);
            Assert.DoesNotContain(withoutOem, r => r.Item.Code == "OEM-01");
            Assert.Equal("NO-MFR", Assert.Single(await service.GetRowsAsync(CatalogQualityFilter.WithoutManufacturer)).Item.Code);
            Assert.Equal("NO-APP", Assert.Single(await service.GetRowsAsync(CatalogQualityFilter.WithoutApplicability)).Item.Code);
            Assert.Equal(4, (await service.GetRowsAsync(CatalogQualityFilter.All)).Count);

            var counts = await service.GetQualityCountsAsync();
            Assert.Equal(4, counts.Total);
            Assert.Equal(1, counts.WithoutApplicability);
            Assert.Equal(1, counts.WithoutManufacturer);
            Assert.Equal(3, counts.WithoutOem);
        }
    }

    [Fact]
    public async Task БезOem_УчитываетТолькоНомераТипаOem()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var service = new CatalogService(db, new OperationsService(db));
            var item = await service.AddItemAsync("ART-01", "Только артикул", UnitType.Piece, null, null, null);
            await service.AddNumberAsync(item.Id, NumberType.Article, "A-100", null);

            // Артикул есть, но OEM нет — позиция должна попадать под фильтр.
            Assert.Single(await service.GetRowsAsync(CatalogQualityFilter.WithoutOem));
        }
    }

    [Fact]
    public async Task ПоискПоКоду_ТочноеСовпадениеБезРегистра()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var service = new CatalogService(db, new OperationsService(db));
            await CreateItemAsync(db, service, "P001");

            Assert.NotNull(await service.FindByCodeAsync(" p001 "));
            Assert.Null(await service.FindByCodeAsync("P"));
        }
    }
}
