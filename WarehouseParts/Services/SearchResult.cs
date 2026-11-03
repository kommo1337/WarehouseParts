using WarehouseParts.Data;

namespace WarehouseParts.Services;

public enum SearchResultType
{
    DirectMatch,
    Analog
}

/// <summary>Подписи типа результата для отображения в интерфейсе.</summary>
public static class SearchResultTypes
{
    public const string DirectMatch = "Прямое совпадение";
    public const string Analog = "Аналог";
}

public class SearchResultRow
{
    public Item Item { get; set; } = null!;

    /// <summary>Текущий остаток на момент поиска.</summary>
    public int Stock { get; set; }

    public string UnitDisplay => DisplayTexts.For(Item.Unit);

    public string ManufacturerDisplay => string.IsNullOrWhiteSpace(Item.Manufacturer) ? "—" : Item.Manufacturer;

    public string ApplicabilityDisplay => string.IsNullOrWhiteSpace(Item.CarApplicability) ? "—" : Item.CarApplicability;

    public string ParametersDisplay
    {
        get
        {
            var parts = new List<string>();
            if (Item.Diameter is not null)
            {
                parts.Add($"Ø {Item.Diameter} мм");
            }
            if (Item.Thickness is not null)
            {
                parts.Add($"толщина {Item.Thickness} мм");
            }
            if (Item.Width is not null)
            {
                parts.Add($"ширина {Item.Width} мм");
            }
            return parts.Count == 0 ? "—" : string.Join(", ", parts);
        }
    }

    public string Status => StockStatus.FromStock(Stock);

    public SearchResultType ResultType { get; set; }

    public string ResultTypeDisplay => ResultType == SearchResultType.DirectMatch
        ? SearchResultTypes.DirectMatch
        : SearchResultTypes.Analog;

    /// <summary>
    /// Исходный номер, по которому найдена позиция или связь с аналогом;
    /// несколько номеров объединены через «, ». Пусто, если неприменимо.
    /// </summary>
    public string MatchedNumber { get; set; } = "";
}

public class SearchResult
{
    public IReadOnlyList<SearchResultRow> Rows { get; }

    public int Total => Rows.Count;

    public int DirectCount => Rows.Count(r => r.ResultType == SearchResultType.DirectMatch);

    public int AnalogCount => Rows.Count(r => r.ResultType == SearchResultType.Analog);

    public SearchResult(IReadOnlyList<SearchResultRow> rows)
    {
        Rows = rows;
    }
}

/// <summary>Сводка по одному номеру в массовом поиске.</summary>
public class MassSearchEntry
{
    public required string RawNumber { get; init; }

    public required IReadOnlyList<SearchResultRow> Rows { get; init; }

    public int DirectCount => Rows.Count(r => r.ResultType == SearchResultType.DirectMatch);

    public int AnalogCount => Rows.Count(r => r.ResultType == SearchResultType.Analog);
}

/// <summary>Итог массового поиска: сводка по каждому номеру и список не найденных.</summary>
public class MassSearchResult
{
    public required IReadOnlyList<MassSearchEntry> Entries { get; init; }

    public IReadOnlyList<string> NotFoundNumbers { get; init; } = Array.Empty<string>();

    public int FoundNumberCount => Entries.Count;

    public int TotalRowCount => Entries.Sum(e => e.Rows.Count);
}
