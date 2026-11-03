using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.Tests;

public class MassSearchTests
{
    [Fact]
    public void Парсер_РазбиваетПоСтрокамИЗапятым_СохраняетНаписание()
    {
        var numbers = NumberListParser.Parse(" 123-ABC \nDEF 456;789,ABC\r\n\n");

        Assert.Equal(new[] { "123-ABC", "DEF 456", "789", "ABC" }, numbers);
    }

    [Fact]
    public void Парсер_ПустойИЛишняяПунктуация_НеСчитаютсяНомерами()
    {
        Assert.Empty(NumberListParser.Parse(null));
        Assert.Empty(NumberListParser.Parse("   \n , ; - "));
        Assert.False(NumberListParser.LooksLikeList("12345"));
        Assert.True(NumberListParser.LooksLikeList("12345, 67890"));
        Assert.True(NumberListParser.LooksLikeList("12345\n67890"));
    }

    [Fact]
    public async Task МассовыйПоиск_НаходитПоНомеруИОтделяетНеНайденные()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var catalog = new CatalogService(db, new OperationsService(db));
            var item = await catalog.AddItemAsync("P-1", "Фильтр масляный", UnitType.Piece, null, null, null, null, null, null);
            await catalog.AddNumberAsync(item.Id, NumberType.Oem, "123-ABC", null);
            var search = new SearchService(db, new OperationsService(db));

            var result = await search.SearchManyAsync(new[] { "123-ABC", "000-000" });

            Assert.Equal(1, result.FoundNumberCount);
            Assert.Single(result.NotFoundNumbers);
            Assert.Equal("000-000", result.NotFoundNumbers[0]);
            var entry = Assert.Single(result.Entries);
            Assert.Equal("123-ABC", entry.RawNumber);
            Assert.Equal(1, entry.DirectCount);
        }
    }

    [Fact]
    public async Task МассовыйПоиск_ОбъединяетДубликатыПоНормализованномуЗначению()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var catalog = new CatalogService(db, new OperationsService(db));
            var item = await catalog.AddItemAsync("P-1", "Фильтр масляный", UnitType.Piece, null, null, null, null, null, null);
            await catalog.AddNumberAsync(item.Id, NumberType.Oem, "ABC 123", null);
            var search = new SearchService(db, new OperationsService(db));

            var result = await search.SearchManyAsync(new[] { "ABC 123", "abc-123", "ABC123" });

            var entry = Assert.Single(result.Entries);
            Assert.Equal("ABC 123", entry.RawNumber);
            Assert.Empty(result.NotFoundNumbers);
        }
    }

    [Fact]
    public async Task МассовыйПоиск_ПустойСписок_ВозвращаетПустойРезультат()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var search = new SearchService(db, new OperationsService(db));

            var result = await search.SearchManyAsync(Array.Empty<string>());

            Assert.Equal(0, result.FoundNumberCount);
            Assert.Empty(result.NotFoundNumbers);
        }
    }
}
