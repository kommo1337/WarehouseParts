using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;

namespace WarehouseParts.Services;

/// <summary>Строка каталога: позиция с вычисленным остатком и статусом.</summary>
public class CatalogItemRow
{
    public Item Item { get; set; } = null!;

    public int Stock { get; set; }

    /// <summary>Подсветка последней добавленной позиции в списке.</summary>
    public bool IsLastAdded { get; set; }

    public string UnitDisplay => DisplayTexts.For(Item.Unit);

    public string Status => StockStatus.FromStock(Stock);
}

/// <summary>Фильтр качества данных каталога.</summary>
public enum CatalogQualityFilter
{
    All,
    WithoutOem,
    WithoutManufacturer,
    WithoutApplicability,
}

/// <summary>Количество позиций с незаполненными данными для фильтров качества.</summary>
public class CatalogQualityCounts
{
    public int Total { get; set; }

    public int WithoutOem { get; set; }

    public int WithoutManufacturer { get; set; }

    public int WithoutApplicability { get; set; }
}

/// <summary>Итог массового ввода номеров.</summary>
public class BulkAddResult
{
    public int Added { get; set; }

    public int SkippedDuplicates { get; set; }
}

/// <summary>
/// Каталог позиций и их номеров: добавление, редактирование, удаление,
/// нормализация и контроль дубликатов номеров, массовый ввод списком.
/// </summary>
public class CatalogService
{
    public const string DuplicateCodeMessage = "Позиция с таким кодом уже существует";
    public const string DuplicateNumberMessage = "Такой номер этого типа уже есть у этой запчасти";
    public const string ItemHasOperationsMessage = "Нельзя удалить позицию: по ней есть операции в журнале";

    private readonly AppDbContext _db;
    private readonly OperationsService _operations;

    public CatalogService(AppDbContext db, OperationsService operations)
    {
        _db = db;
        _operations = operations;
    }

    public async Task<List<CatalogItemRow>> GetRowsAsync(CancellationToken cancellationToken = default)
        => await GetRowsAsync(CatalogQualityFilter.All, cancellationToken);

    /// <summary>
    /// Строки каталога с фильтром качества данных на уровне запроса.
    /// «Без OEM» — отсутствие именно номера типа OEM, а не любого номера.
    /// </summary>
    public async Task<List<CatalogItemRow>> GetRowsAsync(
        CatalogQualityFilter qualityFilter,
        CancellationToken cancellationToken = default)
    {
        var items = _db.Items.AsNoTracking();
        switch (qualityFilter)
        {
            case CatalogQualityFilter.WithoutOem:
                items = items.Where(i => !i.PartNumbers.Any(n => n.NumberType == NumberType.Oem));
                break;
            case CatalogQualityFilter.WithoutManufacturer:
                items = items.Where(i => i.Manufacturer == null || i.Manufacturer.Trim() == string.Empty);
                break;
            case CatalogQualityFilter.WithoutApplicability:
                items = items.Where(i => i.CarApplicability == null || i.CarApplicability.Trim() == string.Empty);
                break;
        }

        var list = await items.OrderBy(i => i.Code).ToListAsync(cancellationToken);
        var stocks = await _operations.GetStocksAsync(list.Select(i => i.Id), cancellationToken);
        return list.Select(item => new CatalogItemRow
        {
            Item = item,
            Stock = stocks.TryGetValue(item.Id, out var stock) ? stock : 0
        }).ToList();
    }

    /// <summary>Счётчики проблемных позиций для фильтров качества данных.</summary>
    public async Task<CatalogQualityCounts> GetQualityCountsAsync(CancellationToken cancellationToken = default)
    {
        var items = _db.Items.AsNoTracking();
        return new CatalogQualityCounts
        {
            Total = await items.CountAsync(cancellationToken),
            WithoutOem = await items
                .CountAsync(i => !i.PartNumbers.Any(n => n.NumberType == NumberType.Oem), cancellationToken),
            WithoutManufacturer = await items
                .CountAsync(i => i.Manufacturer == null || i.Manufacturer.Trim() == string.Empty, cancellationToken),
            WithoutApplicability = await items
                .CountAsync(i => i.CarApplicability == null || i.CarApplicability.Trim() == string.Empty, cancellationToken),
        };
    }

    public async Task<Item> AddItemAsync(
        string code,
        string name,
        UnitType unit,
        string? manufacturer,
        string? applicability,
        string? comment,
        decimal? diameter = null,
        decimal? thickness = null,
        decimal? width = null,
        CancellationToken cancellationToken = default)
    {
        code = (code ?? string.Empty).Trim();
        name = (name ?? string.Empty).Trim();
        ValidateItemFields(code, name);
        await EnsureCodeUniqueAsync(code, excludeItemId: null, cancellationToken);

        var item = new Item
        {
            Code = code,
            Name = name,
            Unit = unit,
            Manufacturer = TrimToNull(manufacturer),
            CarApplicability = TrimToNull(applicability),
            Comment = TrimToNull(comment),
            Diameter = diameter,
            Thickness = thickness,
            Width = width
        };
        _db.Items.Add(item);
        await _db.SaveChangesAsync(cancellationToken);
        return item;
    }

    public async Task UpdateItemAsync(
        int itemId,
        string code,
        string name,
        UnitType unit,
        string? manufacturer,
        string? applicability,
        string? comment,
        decimal? diameter = null,
        decimal? thickness = null,
        decimal? width = null,
        CancellationToken cancellationToken = default)
    {
        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken)
            ?? throw new DomainValidationException("Позиция не найдена");

        code = (code ?? string.Empty).Trim();
        name = (name ?? string.Empty).Trim();
        ValidateItemFields(code, name);
        await EnsureCodeUniqueAsync(code, itemId, cancellationToken);

        item.Code = code;
        item.Name = name;
        item.Unit = unit;
        item.Manufacturer = TrimToNull(manufacturer);
        item.CarApplicability = TrimToNull(applicability);
        item.Comment = TrimToNull(comment);
        item.Diameter = diameter;
        item.Thickness = thickness;
        item.Width = width;
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteItemAsync(int itemId, CancellationToken cancellationToken = default)
    {
        var hasOperations = await _db.Operations.AnyAsync(o => o.ItemId == itemId, cancellationToken);
        if (hasOperations)
        {
            throw new DomainValidationException(ItemHasOperationsMessage);
        }

        var item = await _db.Items.FirstOrDefaultAsync(i => i.Id == itemId, cancellationToken)
            ?? throw new DomainValidationException("Позиция не найдена");
        _db.Items.Remove(item);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<PartNumber>> GetNumbersAsync(int itemId, CancellationToken cancellationToken = default)
        => await _db.PartNumbers.AsNoTracking()
            .Where(n => n.ItemId == itemId)
            .OrderBy(n => n.NumberType)
            .ThenBy(n => n.NumberRaw)
            .ToListAsync(cancellationToken);

    public async Task<PartNumber> AddNumberAsync(
        int itemId,
        NumberType type,
        string raw,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        ValidateNumberRaw(raw, out var normalized);
        await EnsureNumberKeyUniqueAsync(itemId, type, normalized, excludeNumberId: null, cancellationToken);

        var number = new PartNumber
        {
            ItemId = itemId,
            NumberType = type,
            NumberRaw = raw.Trim(),
            NumberNormalized = normalized,
            Comment = TrimToNull(comment)
        };
        _db.PartNumbers.Add(number);
        await _db.SaveChangesAsync(cancellationToken);
        return number;
    }

    public async Task UpdateNumberAsync(
        int numberId,
        NumberType type,
        string raw,
        string? comment,
        CancellationToken cancellationToken = default)
    {
        var number = await _db.PartNumbers.FirstOrDefaultAsync(n => n.Id == numberId, cancellationToken)
            ?? throw new DomainValidationException("Номер не найден");

        ValidateNumberRaw(raw, out var normalized);
        await EnsureNumberKeyUniqueAsync(number.ItemId, type, normalized, numberId, cancellationToken);

        number.NumberType = type;
        number.NumberRaw = raw.Trim();
        number.NumberNormalized = normalized;
        number.Comment = TrimToNull(comment);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task DeleteNumberAsync(int numberId, CancellationToken cancellationToken = default)
    {
        var number = await _db.PartNumbers.FirstOrDefaultAsync(n => n.Id == numberId, cancellationToken)
            ?? throw new DomainValidationException("Номер не найден");
        _db.PartNumbers.Remove(number);
        await _db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Массовый ввод номеров списком: каждая непустая строка с буквами или цифрами —
    /// отдельный номер выбранного типа. Пустые строки и строки без букв/цифр игнорируются.
    /// Полные дубликаты ключа внутри позиции (существующие или внутри списка) пропускаются.
    /// </summary>
    public async Task<BulkAddResult> BulkAddNumbersAsync(
        int itemId,
        NumberType type,
        string text,
        CancellationToken cancellationToken = default)
    {
        var existingKeys = await _db.PartNumbers.AsNoTracking()
            .Where(n => n.ItemId == itemId)
            .Select(n => new { n.NumberType, n.NumberNormalized })
            .ToListAsync(cancellationToken);
        var keys = existingKeys.Select(k => (k.NumberType, k.NumberNormalized)).ToHashSet();

        var result = new BulkAddResult();
        foreach (var line in (text ?? string.Empty).Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var raw = line.Trim();
            var normalized = NumberNormalizer.Normalize(raw);
            if (normalized.Length == 0)
            {
                continue;
            }

            var key = (type, normalized);
            if (!keys.Add(key))
            {
                result.SkippedDuplicates++;
                continue;
            }

            _db.PartNumbers.Add(new PartNumber
            {
                ItemId = itemId,
                NumberType = type,
                NumberRaw = raw,
                NumberNormalized = normalized
            });
            result.Added++;
        }

        await _db.SaveChangesAsync(cancellationToken);
        return result;
    }

    /// <summary>Автоподбор позиций по части кода или наименования.</summary>
    public async Task<List<Item>> SearchItemsAsync(string? filter, CancellationToken cancellationToken = default)
    {
        var text = (filter ?? string.Empty).Trim();
        var items = _db.Items.AsNoTracking();
        if (text.Length > 0)
        {
            items = items.Where(i =>
                AppDbContext.ContainsAnyIgnoreCase(i.Code, i.Name, null, null, text)
                || i.Code + " — " + i.Name == text);
        }

        return await items.OrderBy(i => i.Code).ToListAsync(cancellationToken);
    }

    /// <summary>Точный поиск позиции по номеру запчасти без учета регистра и внешних пробелов.</summary>
    public async Task<Item?> FindByCodeAsync(string code, CancellationToken cancellationToken = default)
    {
        var trimmed = (code ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        return await _db.Items.AsNoTracking()
            .FirstOrDefaultAsync(i => AppDbContext.EqualsIgnoreCase(i.Code.Trim(), trimmed), cancellationToken);
    }

    private static void ValidateItemFields(string code, string name)
    {
        if (code.Length == 0)
        {
            throw new DomainValidationException("Введите номер запчасти");
        }

        if (name.Length == 0)
        {
            throw new DomainValidationException("Введите наименование");
        }
    }

    private static void ValidateNumberRaw(string raw, out string normalized)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            throw new DomainValidationException("Введите номер");
        }

        normalized = NumberNormalizer.Normalize(raw);
        if (normalized.Length == 0)
        {
            throw new DomainValidationException("Номер должен содержать буквы или цифры");
        }
    }

    private async Task EnsureCodeUniqueAsync(string code, int? excludeItemId, CancellationToken cancellationToken)
    {
        var duplicate = await _db.Items.AnyAsync(
            i => i.Id != excludeItemId && AppDbContext.EqualsIgnoreCase(i.Code.Trim(), code),
            cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException(DuplicateCodeMessage);
        }
    }

    private async Task EnsureNumberKeyUniqueAsync(
        int itemId,
        NumberType type,
        string normalized,
        int? excludeNumberId,
        CancellationToken cancellationToken)
    {
        var duplicate = await _db.PartNumbers.AnyAsync(
            n => n.ItemId == itemId
                && n.Id != excludeNumberId
                && n.NumberType == type
                && n.NumberNormalized == normalized,
            cancellationToken);
        if (duplicate)
        {
            throw new DomainValidationException(DuplicateNumberMessage);
        }
    }

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }
}
