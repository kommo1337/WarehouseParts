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
