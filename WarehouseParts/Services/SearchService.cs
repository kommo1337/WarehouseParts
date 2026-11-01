using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;

namespace WarehouseParts.Services;

/// <summary>
/// Поиск позиций каталога: быстрый и расширенный режимы с обязательным
/// одноэтапным расширением аналогами, а также связанные позиции
/// для окна «Номера и аналоги позиции». Транзитивное расширение не применяется.
/// </summary>
public class SearchService
{
    private readonly AppDbContext _db;
    private readonly OperationsService _operations;

    public SearchService(AppDbContext db, OperationsService operations)
    {
        _db = db;
        _operations = operations;
    }

    /// <summary>
    /// Быстрый поиск: подстрока без учета регистра в коде, наименовании, производителе,
    /// применимости или нормализованном номере любого типа. В запросе из нескольких слов
    /// каждое слово должно встречаться у позиции в текстовых полях или номерах.
    /// Пустой запрос = условий нет, прямым результатом считается весь каталог.
    /// </summary>
    public async Task<SearchResult> SearchQuickAsync(string? query, CancellationToken cancellationToken = default)
    {
        var text = (query ?? string.Empty).Trim();
        var normalizedQuery = NumberNormalizer.Normalize(text);
        var items = _db.Items.AsNoTracking();
        if (text.Length == 0)
        {
            return await ExecuteSearchAsync(items, normalizedQuery, NumberTypeFilter.Any, null, cancellationToken);
        }

        var direct = await LoadQuickDirectAsync(items, text, normalizedQuery, cancellationToken);
        return await ExecuteDirectSearchAsync(
            direct,
            BuildQuickNumberKeys(text, normalizedQuery),
            NumberTypeFilter.Any,
            cancellationToken);
    }

    /// <summary>
    /// Расширенный поиск: заполненные условия объединяются по И.
    /// Поле номера, визуально заполненное знаками пунктуации (нормализуется в пустую
    /// строку), считается присутствующим условием, которому ничего не соответствует.
    /// </summary>
    public async Task<SearchResult> SearchAdvancedAsync(SearchCriteria criteria, CancellationToken cancellationToken = default)
    {
        var name = criteria.Name?.Trim() ?? string.Empty;
        var code = criteria.Code?.Trim() ?? string.Empty;
        var manufacturer = criteria.Manufacturer?.Trim() ?? string.Empty;
        var applicability = criteria.CarApplicability?.Trim() ?? string.Empty;
        var numberPresent = !string.IsNullOrWhiteSpace(criteria.Number);
        var normalizedNumber = NumberNormalizer.Normalize(criteria.Number);

        if (numberPresent && normalizedNumber.Length == 0)
        {
            return new SearchResult(Array.Empty<SearchResultRow>());
        }

        var items = _db.Items.AsNoTracking();
        if (name.Length > 0)
        {
            items = items.Where(item => AppDbContext.ContainsIgnoreCase(item.Name, name));
        }

        if (code.Length > 0)
        {
            items = items.Where(item => AppDbContext.EqualsIgnoreCase(item.Code.Trim(), code));
        }

        if (manufacturer.Length > 0)
        {
            items = items.Where(item => AppDbContext.ContainsIgnoreCase(item.Manufacturer, manufacturer));
        }

        if (applicability.Length > 0)
        {
            items = items.Where(item => AppDbContext.ContainsIgnoreCase(item.CarApplicability, applicability));
        }

        if (numberPresent)
        {
            if (criteria.NumberFilter == NumberTypeFilter.Any)
            {
                items = items.Where(item => _db.PartNumbers.Any(n =>
                    n.ItemId == item.Id && n.NumberNormalized.Contains(normalizedNumber)));
            }
            else
            {
                var numberType = criteria.NumberFilter switch
                {
                    NumberTypeFilter.Oem => NumberType.Oem,
                    NumberTypeFilter.Article => NumberType.Article,
                    _ => NumberType.Analog,
                };
                items = items.Where(item => _db.PartNumbers.Any(n =>
                    n.ItemId == item.Id
                    && n.NumberNormalized.Contains(normalizedNumber)
                    && n.NumberType == numberType));
            }
        }

        items = ApplyAvailability(items, criteria.Availability);

        return await ExecuteSearchAsync(
            items,
            normalizedNumber,
            criteria.NumberFilter,
            item => ParameterMatches(item.Diameter, criteria.Diameter, criteria.TolerancePercent)
                && ParameterMatches(item.Thickness, criteria.Thickness, criteria.TolerancePercent)
                && ParameterMatches(item.Width, criteria.Width, criteria.TolerancePercent),
            cancellationToken);
    }

    /// <summary>Сводные показатели каталога для экрана поиска.</summary>
    public async Task<(int Total, int InStock, int OutOfStock)> GetCatalogCountsAsync(CancellationToken cancellationToken = default)
    {
        var total = await _db.Items.CountAsync(cancellationToken);
        var inStock = await _db.Items.CountAsync(item =>
            (_db.Operations
                .Where(o => o.ItemId == item.Id)
                .Select(o => (int?)(o.OperationType == OperationType.Receipt ? o.Quantity : -o.Quantity))
                .Sum() ?? 0) > 0,
            cancellationToken);
        return (total, inStock, total - inStock);
    }

    /// <summary>
    /// Связанные позиции для окна «Номера и аналоги позиции»:
    /// все позиции каталога (кроме данной), имеющие хотя бы один общий ключ
    /// (тип номера + нормализованный номер). Связь строится ровно в один шаг.
    /// </summary>
    public async Task<List<RelatedItemRow>> GetRelatedItemsAsync(int itemId, CancellationToken cancellationToken = default)
    {
        var itemNumbers = await _db.PartNumbers.AsNoTracking()
            .Where(n => n.ItemId == itemId)
            .ToListAsync(cancellationToken);
        var keys = itemNumbers
            .Select(n => (n.NumberType, n.NumberNormalized))
            .ToHashSet();
        if (keys.Count == 0)
        {
            return new List<RelatedItemRow>();
        }

        var normalizedKeys = keys.Select(k => k.NumberNormalized).Distinct().ToList();
        var candidateNumbers = await _db.PartNumbers.AsNoTracking()
            .Where(n => n.ItemId != itemId && normalizedKeys.Contains(n.NumberNormalized))
            .ToListAsync(cancellationToken);
        var commonByItem = candidateNumbers
            .Where(n => keys.Contains((n.NumberType, n.NumberNormalized)))
            .GroupBy(n => n.ItemId)
            .ToDictionary(g => g.Key, g => JoinRawNumbers(g));
        if (commonByItem.Count == 0)
        {
            return new List<RelatedItemRow>();
        }

        var relatedIds = commonByItem.Keys.ToList();
        var items = await _db.Items.AsNoTracking()
            .Where(i => relatedIds.Contains(i.Id))
            .ToListAsync(cancellationToken);
        var stocks = await _operations.GetStocksAsync(relatedIds, cancellationToken);

        return items
            .Select(item => new RelatedItemRow
            {
                Item = item,
                Stock = GetStock(stocks, item.Id),
                CommonNumbers = commonByItem[item.Id]
            })
            .OrderBy(r => r.Item.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private IQueryable<Item> ApplyAvailability(IQueryable<Item> items, AvailabilityFilter filter)
    {
        if (filter == AvailabilityFilter.InStock)
        {
            return items.Where(item =>
                (_db.Operations
                    .Where(o => o.ItemId == item.Id)
                    .Select(o => (int?)(o.OperationType == OperationType.Receipt ? o.Quantity : -o.Quantity))
                    .Sum() ?? 0) > 0);
        }

        if (filter == AvailabilityFilter.OutOfStock)
        {
            return items.Where(item =>
                (_db.Operations
                    .Where(o => o.ItemId == item.Id)
                    .Select(o => (int?)(o.OperationType == OperationType.Receipt ? o.Quantity : -o.Quantity))
                    .Sum() ?? 0) == 0);
        }

        return items;
    }

    private async Task<SearchResult> ExecuteSearchAsync(
        IQueryable<Item> directQuery,
        string normalizedNumber,
        NumberTypeFilter numberFilter,
        Func<Item, bool>? directFilter,
        CancellationToken cancellationToken)
    {
        var direct = await directQuery.ToListAsync(cancellationToken);
        if (directFilter is not null)
        {
            direct = direct.Where(directFilter).ToList();
        }

        return await ExecuteDirectSearchAsync(
            direct,
            new[] { normalizedNumber },
            numberFilter,
            cancellationToken);
    }

    private async Task<SearchResult> ExecuteDirectSearchAsync(
        List<Item> direct,
        IReadOnlyList<string> normalizedNumbers,
        NumberTypeFilter numberFilter,
        CancellationToken cancellationToken)
    {
        if (direct.Count == 0)
        {
            return new SearchResult(Array.Empty<SearchResultRow>());
        }

        var directIds = direct.Select(i => i.Id).ToHashSet();
        var matchedNumbers = await GetMatchedNumbersAsync(
            directIds,
            normalizedNumbers,
            numberFilter,
            cancellationToken);

        var allItemsAreDirect = direct.Count == await _db.Items.CountAsync(cancellationToken);
        var commonByItem = allItemsAreDirect
            ? new Dictionary<int, string>()
            : await GetCommonNumbersAsync(directIds, cancellationToken);
        var analogIds = commonByItem.Keys.ToList();
        var analogItems = analogIds.Count == 0
            ? new List<Item>()
            : await _db.Items.AsNoTracking()
                .Where(i => analogIds.Contains(i.Id))
                .ToListAsync(cancellationToken);
        var stocks = await _operations.GetStocksAsync(directIds.Concat(analogIds), cancellationToken);

        var rows = direct.Select(item => new SearchResultRow
        {
            Item = item,
            Stock = GetStock(stocks, item.Id),
            ResultType = SearchResultType.DirectMatch,
            MatchedNumber = matchedNumbers.TryGetValue(item.Id, out var matched) ? matched : string.Empty
        }).ToList();

        rows.AddRange(analogItems.Select(item => new SearchResultRow
        {
            Item = item,
            Stock = GetStock(stocks, item.Id),
            ResultType = SearchResultType.Analog,
            MatchedNumber = commonByItem[item.Id]
        }));

        var ordered = rows
            .OrderBy(r => r.ResultType == SearchResultType.DirectMatch ? 0 : 1)
            .ThenBy(r => r.Item.Code, StringComparer.OrdinalIgnoreCase)
            .ToList();
        return new SearchResult(ordered);
    }

    private async Task<List<Item>> LoadQuickDirectAsync(
        IQueryable<Item> items,
        string text,
        string normalizedQuery,
        CancellationToken cancellationToken)
    {
        var direct = await items.Where(item =>
                AppDbContext.ContainsAnyIgnoreCase(item.Code, item.Name, item.Manufacturer, item.CarApplicability, text)
                || (normalizedQuery.Length > 0 && _db.PartNumbers.Any(n =>
                    n.ItemId == item.Id && n.NumberNormalized.Contains(normalizedQuery))))
            .ToListAsync(cancellationToken);

        var tokens = SplitSearchTokens(text);
        if (tokens.Count <= 1)
        {
            return direct;
        }

        var tokenItems = items;
        foreach (var token in tokens)
        {
            var rawToken = token;
            var normalizedToken = NumberNormalizer.Normalize(token);
            tokenItems = tokenItems.Where(item =>
                AppDbContext.ContainsAnyIgnoreCase(item.Code, item.Name, item.Manufacturer, item.CarApplicability, rawToken)
                || (normalizedToken.Length > 0 && _db.PartNumbers.Any(n =>
                    n.ItemId == item.Id && n.NumberNormalized.Contains(normalizedToken))));
        }

        var tokenDirect = await tokenItems.ToListAsync(cancellationToken);
        return MergeDirectItems(direct, tokenDirect);
    }

    private static IReadOnlyList<string> BuildQuickNumberKeys(string text, string normalizedQuery)
    {
        var keys = new List<string>();
        if (normalizedQuery.Length > 0)
        {
            keys.Add(normalizedQuery);
        }

        var tokens = SplitSearchTokens(text);
        if (tokens.Count > 1)
        {
            keys.AddRange(tokens.Select(NumberNormalizer.Normalize));
        }

        return keys.Where(k => k.Length > 0).Distinct(StringComparer.Ordinal).ToList();
    }

    private static List<string> SplitSearchTokens(string text)
        => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    private static List<Item> MergeDirectItems(List<Item> first, List<Item> second)
    {
        if (second.Count == 0)
        {
            return first;
        }

        var ids = first.Select(i => i.Id).ToHashSet();
        first.AddRange(second.Where(i => ids.Add(i.Id)));
        return first;
    }

    private async Task<IReadOnlyDictionary<int, string>> GetMatchedNumbersAsync(
        IReadOnlySet<int> directIds,
        IReadOnlyList<string> normalizedNumbers,
        NumberTypeFilter filter,
        CancellationToken cancellationToken)
    {
        var keys = normalizedNumbers.Where(n => n.Length > 0).Distinct(StringComparer.Ordinal).ToList();
        if (keys.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var numbers = await _db.PartNumbers.AsNoTracking()
            .Where(n => directIds.Contains(n.ItemId) && keys.Any(key => n.NumberNormalized.Contains(key)))
            .ToListAsync(cancellationToken);
        return numbers
            .Where(n => NumberFilterMatches(n.NumberType, filter)
                && keys.Any(key => n.NumberNormalized.Contains(key)))
            .GroupBy(n => n.ItemId)
            .ToDictionary(g => g.Key, g => JoinRawNumbers(g));
    }

    private async Task<IReadOnlyDictionary<int, string>> GetCommonNumbersAsync(
        IReadOnlySet<int> directIds,
        CancellationToken cancellationToken)
    {
        var directNumbers = await _db.PartNumbers.AsNoTracking()
            .Where(n => directIds.Contains(n.ItemId))
            .ToListAsync(cancellationToken);
        var directKeys = directNumbers
            .Select(n => (n.NumberType, n.NumberNormalized))
            .ToHashSet();
        if (directKeys.Count == 0)
        {
            return new Dictionary<int, string>();
        }

        var normalizedKeys = directKeys.Select(k => k.NumberNormalized).Distinct().ToList();
        var candidateNumbers = await _db.PartNumbers.AsNoTracking()
            .Where(n => !directIds.Contains(n.ItemId) && normalizedKeys.Contains(n.NumberNormalized))
            .ToListAsync(cancellationToken);
        return candidateNumbers
            .Where(n => directKeys.Contains((n.NumberType, n.NumberNormalized)))
            .GroupBy(n => n.ItemId)
            .ToDictionary(g => g.Key, g => JoinRawNumbers(g));
    }

    private static bool NumberFilterMatches(NumberType numberType, NumberTypeFilter filter) => filter switch
    {
        NumberTypeFilter.Oem => numberType == NumberType.Oem,
        NumberTypeFilter.Article => numberType == NumberType.Article,
        NumberTypeFilter.Analog => numberType == NumberType.Analog,
        _ => true,
    };

    private static bool ParameterMatches(decimal? itemValue, decimal? searchValue, decimal tolerancePercent)
    {
        if (!searchValue.HasValue)
        {
            return true;
        }

        if (!itemValue.HasValue)
        {
            return false;
        }

        var tolerance = Math.Abs(searchValue.Value) * Math.Abs(tolerancePercent) / 100m;
        return Math.Abs(itemValue.Value - searchValue.Value) <= tolerance;
    }

    private static int GetStock(IReadOnlyDictionary<int, int> stocks, int itemId)
        => stocks.TryGetValue(itemId, out var stock) ? stock : 0;

    private static string JoinRawNumbers(IEnumerable<PartNumber> numbers)
        => string.Join(", ", numbers.Select(n => n.NumberRaw).Distinct(StringComparer.Ordinal));
}
