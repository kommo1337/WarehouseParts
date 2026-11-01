using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.Tests;

public class SearchServiceTests
{
    private static async Task<Item> CreateItemAsync(
        AppDbContext db,
        string code,
        string name,
        string? manufacturer = null,
        string? applicability = null)
    {
        var item = new Item
        {
            Code = code,
            Name = name,
            Unit = UnitType.Piece,
            Manufacturer = manufacturer,
            CarApplicability = applicability
        };
        db.Items.Add(item);
        await db.SaveChangesAsync();
        return item;
    }

    private static async Task AddNumberAsync(AppDbContext db, int itemId, NumberType type, string raw)
    {
        db.PartNumbers.Add(new PartNumber
        {
            ItemId = itemId,
            NumberType = type,
            NumberRaw = raw,
            NumberNormalized = NumberNormalizer.Normalize(raw)
        });
        await db.SaveChangesAsync();
    }

    // Набор позиций и номеров по разделу 12.3 ТЗ:
    // A: OEM-1, AN-1; B: OEM 1, AN-2; C: OEM-2, AN-2; D: OEM-3, AN-3.
    private static async Task<(Item A, Item B, Item C, Item D)> CreateCatalogAbcdAsync(AppDbContext db)
    {
        var a = await CreateItemAsync(db, "P-A", "Фильтр масляный");
        var b = await CreateItemAsync(db, "P-B", "Колодки тормозные");
        var c = await CreateItemAsync(db, "P-C", "Свеча зажигания");
        var d = await CreateItemAsync(db, "P-D", "Ремень ГРМ");

        await AddNumberAsync(db, a.Id, NumberType.Oem, "OEM-1");
        await AddNumberAsync(db, a.Id, NumberType.Analog, "AN-1");
        await AddNumberAsync(db, b.Id, NumberType.Oem, "OEM 1");
        await AddNumberAsync(db, b.Id, NumberType.Analog, "AN-2");
        await AddNumberAsync(db, c.Id, NumberType.Oem, "OEM-2");
        await AddNumberAsync(db, c.Id, NumberType.Analog, "AN-2");
        await AddNumberAsync(db, d.Id, NumberType.Oem, "OEM-3");
        await AddNumberAsync(db, d.Id, NumberType.Analog, "AN-3");

        return (a, b, c, d);
    }

    private static SearchResultRow? FindRow(SearchResult result, int itemId)
        => result.Rows.FirstOrDefault(r => r.Item.Id == itemId);

    [Fact]
    public async Task ПоискНомераСПунктуацией_НаходитОригиналыКакПрямыеБезДублей()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var (a, b, c, d) = await CreateCatalogAbcdAsync(db);
            var service = new SearchService(db, new OperationsService(db));

            var result = await service.SearchQuickAsync("oem1");

            Assert.Equal(2, result.DirectCount);
            Assert.Equal(1, result.AnalogCount);
            Assert.Equal(3, result.Total);
            var rowA = FindRow(result, a.Id)!;
            var rowB = FindRow(result, b.Id)!;
            var rowC = FindRow(result, c.Id)!;
            Assert.Equal(SearchResultType.DirectMatch, rowA.ResultType);
            Assert.Equal(SearchResultType.DirectMatch, rowB.ResultType);
            Assert.Equal("OEM-1", rowA.MatchedNumber);
            Assert.Equal("OEM 1", rowB.MatchedNumber);
            Assert.Equal(SearchResultType.Analog, rowC.ResultType);
            Assert.Equal("AN-2", rowC.MatchedNumber);
            Assert.Null(FindRow(result, d.Id));
            Assert.Equal(result.Total, result.Rows.Select(r => r.Item.Id).Distinct().Count());
        }
    }

    [Fact]
    public async Task ПоискОбщегоНомера_ПрямыеИдутПередАналогамиИСортированыПоКоду()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var (a, b, c, d) = await CreateCatalogAbcdAsync(db);
            var service = new SearchService(db, new OperationsService(db));

            var result = await service.SearchQuickAsync("AN-2");

            Assert.Equal(2, result.DirectCount);
            Assert.Equal(1, result.AnalogCount);
            Assert.Equal(3, result.Total);

            Assert.Equal(b.Id, result.Rows[0].Item.Id);
            Assert.Equal(SearchResultType.DirectMatch, result.Rows[0].ResultType);
            Assert.Equal(c.Id, result.Rows[1].Item.Id);
            Assert.Equal(SearchResultType.DirectMatch, result.Rows[1].ResultType);
            Assert.Equal(a.Id, result.Rows[2].Item.Id);
            Assert.Equal(SearchResultType.Analog, result.Rows[2].ResultType);
            Assert.Equal("OEM-1", result.Rows[2].MatchedNumber);

            Assert.Null(FindRow(result, d.Id));
        }
    }

    [Fact]
    public async Task ТранзитивнаяЦепочка_НеРасширяется()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            // A связана с B ключом K1, B связана с C ключом K2, A и C общих ключей не имеют.
            var a = await CreateItemAsync(db, "P-A", "Щетка стеклоочистителя");
            var b = await CreateItemAsync(db, "P-B", "Позиция B");
            var c = await CreateItemAsync(db, "P-C", "Позиция C");
            await AddNumberAsync(db, a.Id, NumberType.Oem, "K1");
            await AddNumberAsync(db, b.Id, NumberType.Oem, "K1");
            await AddNumberAsync(db, b.Id, NumberType.Analog, "K2");
            await AddNumberAsync(db, c.Id, NumberType.Analog, "K2");
            var service = new SearchService(db, new OperationsService(db));

            // Прямым совпадением является только A.
            var result = await service.SearchQuickAsync("щетка");

            Assert.Equal(1, result.DirectCount);
            Assert.Equal(1, result.AnalogCount);
            Assert.Equal(SearchResultType.DirectMatch, FindRow(result, a.Id)!.ResultType);
            var analogRow = FindRow(result, b.Id)!;
            Assert.Equal(SearchResultType.Analog, analogRow.ResultType);
            Assert.Equal("K1", analogRow.MatchedNumber);
            Assert.Null(FindRow(result, c.Id));
        }
    }

    [Fact]
    public async Task НомераРазличающиесяТолькоПунктуацией_СвязываютПозиции()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var first = await CreateItemAsync(db, "P-1", "Фильтр Mann");
            var second = await CreateItemAsync(db, "P-2", "Фильтр Bosch");
            await AddNumberAsync(db, first.Id, NumberType.Oem, "W 712/52");
            await AddNumberAsync(db, second.Id, NumberType.Oem, "W712.52");
            var service = new SearchService(db, new OperationsService(db));

            // Пунктуационные варианты одного номера нормализуются в общий ключ:
            // обе позиции находятся как прямые по любому варианту написания.
            var byNumber = await service.SearchQuickAsync("W712/52");
            Assert.Equal(2, byNumber.DirectCount);
            Assert.Equal(0, byNumber.AnalogCount);
            Assert.Equal(2, byNumber.Total);
            Assert.Equal("W 712/52", FindRow(byNumber, first.Id)!.MatchedNumber);
            Assert.Equal("W712.52", FindRow(byNumber, second.Id)!.MatchedNumber);

            // Номера связывают позиции: поиск по наименованию одной
            // находит другую как аналог через общий ключ.
            var byName = await service.SearchQuickAsync("Mann");
            Assert.Equal(1, byName.DirectCount);
            Assert.Equal(1, byName.AnalogCount);
            var analogRow = FindRow(byName, second.Id)!;
            Assert.Equal(SearchResultType.Analog, analogRow.ResultType);
            Assert.Equal("W712.52", analogRow.MatchedNumber);
        }
    }

    [Fact]
    public async Task РасширенныйПоиск_УсловияОбъединяютсяПоИ()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            await CreateItemAsync(db, "P-1", "Фильтр салона", manufacturer: "Mann");
            await CreateItemAsync(db, "P-2", "Фильтр масляный", manufacturer: "Bosch");
            var service = new SearchService(db, new OperationsService(db));

            var result = await service.SearchAdvancedAsync(new SearchCriteria
            {
                Name = "фильтр",
                Manufacturer = "MANN"
            });

            var row = Assert.Single(result.Rows);
            Assert.Equal("P-1", row.Item.Code);
            Assert.Equal(SearchResultType.DirectMatch, row.ResultType);
        }
    }

    [Fact]
    public async Task РасширенныйПоиск_ПустыеУсловияВозвращаютВесьКаталог()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            await CreateCatalogAbcdAsync(db);
            var service = new SearchService(db, new OperationsService(db));

            var result = await service.SearchAdvancedAsync(new SearchCriteria());

            Assert.Equal(4, result.Total);
            Assert.Equal(4, result.DirectCount);
            Assert.Equal(0, result.AnalogCount);
        }
    }

    [Fact]
    public async Task РасширенныйПоиск_РегистрНеВлияетНаНомер()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var (a, b, _, _) = await CreateCatalogAbcdAsync(db);
            var service = new SearchService(db, new OperationsService(db));

            var result = await service.SearchAdvancedAsync(new SearchCriteria { Number = "an-1" });

            Assert.Equal(1, result.DirectCount);
            Assert.Equal(SearchResultType.DirectMatch, FindRow(result, a.Id)!.ResultType);
            Assert.Equal(SearchResultType.Analog, FindRow(result, b.Id)!.ResultType);
        }
    }

    [Fact]
    public async Task РасширенныйПоиск_ТипНомераОграничиваетСовпадение()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var (a, b, _, _) = await CreateCatalogAbcdAsync(db);
            var service = new SearchService(db, new OperationsService(db));

            var asOem = await service.SearchAdvancedAsync(new SearchCriteria
            {
                Number = "AN-1",
                NumberFilter = NumberTypeFilter.Oem
            });
            Assert.Empty(asOem.Rows);

            var asAnalog = await service.SearchAdvancedAsync(new SearchCriteria
            {
                Number = "AN-1",
                NumberFilter = NumberTypeFilter.Analog
            });
            Assert.Equal(1, asAnalog.DirectCount);
            Assert.Equal(SearchResultType.DirectMatch, FindRow(asAnalog, a.Id)!.ResultType);
            Assert.Equal(SearchResultType.Analog, FindRow(asAnalog, b.Id)!.ResultType);

            var asAny = await service.SearchAdvancedAsync(new SearchCriteria
            {
                Number = "AN-1",
                NumberFilter = NumberTypeFilter.Any
            });
            Assert.Equal(1, asAny.DirectCount);
            Assert.Equal(SearchResultType.DirectMatch, FindRow(asAny, a.Id)!.ResultType);
        }
    }

    [Fact]
    public async Task РасширенныйПоиск_НомерИзОднойПунктуацииНичегоНеНаходит()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            await CreateCatalogAbcdAsync(db);
            var service = new SearchService(db, new OperationsService(db));

            var result = await service.SearchAdvancedAsync(new SearchCriteria { Number = "-- // .." });

            Assert.Empty(result.Rows);
        }
    }

    [Fact]
    public async Task БыстрыйПоиск_ПоЧастиНаименованияНаходитАналоги()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var (a, b, c, d) = await CreateCatalogAbcdAsync(db);
            var service = new SearchService(db, new OperationsService(db));

            var result = await service.SearchQuickAsync("колодки");

            Assert.Equal(1, result.DirectCount);
            Assert.Equal(SearchResultType.DirectMatch, FindRow(result, b.Id)!.ResultType);
            Assert.Equal(SearchResultType.Analog, FindRow(result, a.Id)!.ResultType);
            Assert.Equal(SearchResultType.Analog, FindRow(result, c.Id)!.ResultType);
            Assert.Null(FindRow(result, d.Id));
        }
    }

    [Fact]
    public async Task БыстрыйПоиск_ПустойЗапросВозвращаетВесьКаталог()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            await CreateCatalogAbcdAsync(db);
            var service = new SearchService(db, new OperationsService(db));

            var result = await service.SearchQuickAsync("   ");

            Assert.Equal(4, result.Total);
            Assert.Equal(4, result.DirectCount);
        }
    }

    [Fact]
    public async Task РасширенныйПоиск_ТочноеСовпадениеКодаБезУчетаРегистраИПробелов()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var (a, _, _, _) = await CreateCatalogAbcdAsync(db);
            var service = new SearchService(db, new OperationsService(db));

            var result = await service.SearchAdvancedAsync(new SearchCriteria { Code = "  p-a  " });

            Assert.Equal(1, result.DirectCount);
            var directRow = result.Rows[0];
            Assert.Equal(a.Id, directRow.Item.Id);
            Assert.Equal(SearchResultType.DirectMatch, directRow.ResultType);
            Assert.Equal(1, result.AnalogCount);

            var partialCode = await service.SearchAdvancedAsync(new SearchCriteria { Code = "P" });
            Assert.Empty(partialCode.Rows);
        }
    }

    [Fact]
    public async Task РасширенныйПоиск_ПрименимостьИПроизводительПоЧастиТекста()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(
                db,
                "P-1",
                "Фильтр салона",
                manufacturer: "MANN-FILTER",
                applicability: "Volkswagen Golf VII, 1.4 TSI, 2012–2020");
            await CreateItemAsync(db, "P-2", "Фильтр масляный", manufacturer: "Bosch");
            var service = new SearchService(db, new OperationsService(db));

            var byApplicability = await service.SearchAdvancedAsync(new SearchCriteria { CarApplicability = "golf vii" });
            Assert.Equal(item.Id, Assert.Single(byApplicability.Rows).Item.Id);

            var byManufacturer = await service.SearchAdvancedAsync(new SearchCriteria { Manufacturer = "mann-fil" });
            Assert.Equal(item.Id, Assert.Single(byManufacturer.Rows).Item.Id);

            var quick = await service.SearchQuickAsync("TSI");
            Assert.Equal(item.Id, Assert.Single(quick.Rows).Item.Id);
        }
    }

    [Fact]
    public async Task ПозицияИПрямаяИАналог_ВыводитсяОдинРазКакПрямая()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var (a, b, _, _) = await CreateCatalogAbcdAsync(db);
            var service = new SearchService(db, new OperationsService(db));

            // B подходит и как прямое совпадение по номеру, и как аналог A.
            var result = await service.SearchQuickAsync("oem1");

            var rowB = FindRow(result, b.Id)!;
            Assert.Equal(SearchResultType.DirectMatch, rowB.ResultType);
            Assert.Equal(1, result.Rows.Count(r => r.Item.Id == b.Id));
        }
    }

    [Fact]
    public async Task РасширенныйПоиск_ФильтрНаличия()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var (a, b, c, d) = await CreateCatalogAbcdAsync(db);
            var operations = new OperationsService(db);
            await operations.AddAsync(OperationType.Receipt, a.Id, 5, null);
            var service = new SearchService(db, operations);

            var inStock = await service.SearchAdvancedAsync(new SearchCriteria { Availability = AvailabilityFilter.InStock });
            Assert.Equal(1, inStock.DirectCount);
            Assert.Equal(a.Id, inStock.Rows[0].Item.Id);
            Assert.Equal(StockStatus.InStock, inStock.Rows[0].Status);

            var outOfStock = await service.SearchAdvancedAsync(new SearchCriteria { Availability = AvailabilityFilter.OutOfStock });
            Assert.Equal(3, outOfStock.DirectCount);
            Assert.Contains(outOfStock.Rows, r => r.Item.Id == b.Id);
            Assert.Contains(outOfStock.Rows, r => r.Item.Id == c.Id);
            Assert.Contains(outOfStock.Rows, r => r.Item.Id == d.Id);
            Assert.All(
                outOfStock.Rows.Where(r => r.ResultType == SearchResultType.DirectMatch),
                r => Assert.Equal(StockStatus.OutOfStock, r.Status));
        }
    }

    [Fact]
    public async Task СвязанныеПозиции_РовноОдинШагСвязи()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var (a, b, c, d) = await CreateCatalogAbcdAsync(db);
            var operations = new OperationsService(db);
            await operations.AddAsync(OperationType.Receipt, b.Id, 3, null);
            var service = new SearchService(db, operations);

            var relatedToA = await service.GetRelatedItemsAsync(a.Id);
            var rowB = Assert.Single(relatedToA);
            Assert.Equal(b.Id, rowB.Item.Id);
            Assert.Equal("OEM 1", rowB.CommonNumbers);
            Assert.Equal(3, rowB.Stock);
            Assert.Equal(StockStatus.InStock, rowB.Status);

            var relatedToB = await service.GetRelatedItemsAsync(b.Id);
            Assert.Equal(2, relatedToB.Count);
            Assert.Equal(a.Id, relatedToB[0].Item.Id);
            Assert.Equal("OEM-1", relatedToB[0].CommonNumbers);
            Assert.Equal(c.Id, relatedToB[1].Item.Id);
            Assert.Equal("AN-2", relatedToB[1].CommonNumbers);

            var relatedToC = await service.GetRelatedItemsAsync(c.Id);
            var rowOnlyB = Assert.Single(relatedToC);
            Assert.Equal(b.Id, rowOnlyB.Item.Id);

            Assert.Empty(await service.GetRelatedItemsAsync(d.Id));
        }
    }

    [Fact]
    public async Task РасширенныйПоиск_ПоПараметрамСРазбросомВПроцентах()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var d73 = await CreateItemAsync(db, "D-73", "Диск 73");
            var d77 = await CreateItemAsync(db, "D-77", "Диск 77");
            var d79 = await CreateItemAsync(db, "D-79", "Диск 79");
            var noParams = await CreateItemAsync(db, "D-NONE", "Диск без параметров");
            d73.Diameter = 73m;
            d77.Diameter = 77m;
            d77.Thickness = 20m;
            d79.Diameter = 79m;
            await db.SaveChangesAsync();
            var service = new SearchService(db, new OperationsService(db));

            // 77 ± 5% → диапазон 73.15–80.85: находятся 77 и 79, 73 снаружи диапазона.
            var withTolerance = await service.SearchAdvancedAsync(new SearchCriteria
            {
                Diameter = 77m,
                TolerancePercent = 5m
            });

            Assert.Equal(2, withTolerance.DirectCount);
            Assert.Contains(withTolerance.Rows, r => r.Item.Id == d77.Id);
            Assert.Contains(withTolerance.Rows, r => r.Item.Id == d79.Id);
            Assert.DoesNotContain(withTolerance.Rows, r => r.Item.Id == d73.Id);
            Assert.DoesNotContain(withTolerance.Rows, r => r.Item.Id == noParams.Id);

            // Без разброса — только точное совпадение.
            var exact = await service.SearchAdvancedAsync(new SearchCriteria
            {
                Diameter = 77m,
                TolerancePercent = 0m
            });
            var exactRow = Assert.Single(exact.Rows);
            Assert.Equal(d77.Id, exactRow.Item.Id);

            // Поиск по толщине не находит позиции без толщины.
            var byThickness = await service.SearchAdvancedAsync(new SearchCriteria
            {
                Thickness = 20m,
                TolerancePercent = 10m
            });
            var thicknessRow = Assert.Single(byThickness.Rows);
            Assert.Equal(d77.Id, thicknessRow.Item.Id);

            // Пустые поля параметров не влияют на результат.
            var noConditions = await service.SearchAdvancedAsync(new SearchCriteria());
            Assert.Equal(4, noConditions.Total);
        }
    }

    [Fact]
    public async Task БыстрыйПоиск_РегистрКириллицыНеВлияетНаРезультат()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db, "P-1", "Фильтр масляный", manufacturer: "Манн");
            var service = new SearchService(db, new OperationsService(db));

            var byName = await service.SearchQuickAsync("ФИЛЬТР");
            Assert.Equal(item.Id, Assert.Single(byName.Rows).Item.Id);

            var byManufacturer = await service.SearchAdvancedAsync(new SearchCriteria { Manufacturer = "МАНН" });
            Assert.Equal(item.Id, Assert.Single(byManufacturer.Rows).Item.Id);
        }
    }

    [Fact]
    public async Task ПоискНомера_ПоЧастиИРазнойПунктуации()
    {
        await using var db = TestDb.CreateContext(out var connection);
        await using (connection)
        {
            var item = await CreateItemAsync(db, "P-1", "Фильтр воздушный", manufacturer: "Toyota");
            var otherToyota = await CreateItemAsync(db, "P-2", "Фильтр масляный", manufacturer: "Toyota");
            var otherManufacturer = await CreateItemAsync(db, "P-3", "Фильтр воздушный", manufacturer: "Nissan");
            await AddNumberAsync(db, item.Id, NumberType.Oem, "17801-F0060");
            await AddNumberAsync(db, otherToyota.Id, NumberType.Oem, "90915-YZZD1");
            await AddNumberAsync(db, otherManufacturer.Id, NumberType.Oem, "17801-F0060");
            var service = new SearchService(db, new OperationsService(db));

            foreach (var query in new[] { "17801-F0060", "17801F0060", "17801 F0060", "F0060", "f0060" })
            {
                var result = await service.SearchQuickAsync(query);
                Assert.Equal(2, result.DirectCount);
                Assert.Equal(
                    new[] { item.Id, otherManufacturer.Id }.OrderBy(id => id),
                    result.Rows.Select(r => r.Item.Id).OrderBy(id => id));
                Assert.All(
                    result.Rows,
                    row => Assert.Equal(SearchResultType.DirectMatch, row.ResultType));
                Assert.Equal("17801-F0060", result.Rows.First(r => r.Item.Id == item.Id).MatchedNumber);
            }

            foreach (var query in new[] { "Toyota 17801-F0060", "TOYOTA 17801 F0060" })
            {
                var result = await service.SearchQuickAsync(query);
                Assert.Equal(2, result.Total);
                Assert.Equal(1, result.DirectCount);
                Assert.Equal(1, result.AnalogCount);
                var directRow = Assert.Single(result.Rows, r => r.Item.Id == item.Id);
                Assert.Equal(SearchResultType.DirectMatch, directRow.ResultType);
                Assert.Equal("17801-F0060", directRow.MatchedNumber);
                var analogRow = Assert.Single(result.Rows, r => r.Item.Id == otherManufacturer.Id);
                Assert.Equal(SearchResultType.Analog, analogRow.ResultType);
                Assert.Equal("17801-F0060", analogRow.MatchedNumber);
            }

            var advanced = await service.SearchAdvancedAsync(new SearchCriteria { Number = "F0060" });
            Assert.Equal(2, advanced.DirectCount);
            Assert.Equal(
                new[] { item.Id, otherManufacturer.Id }.OrderBy(id => id),
                advanced.Rows.Select(r => r.Item.Id).OrderBy(id => id));
            Assert.Equal("17801-F0060", advanced.Rows.First(r => r.Item.Id == item.Id).MatchedNumber);
        }
    }
}
