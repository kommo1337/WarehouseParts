namespace WarehouseParts.Services;

/// <summary>Фильтр типа номера в расширенном поиске.</summary>
public enum NumberTypeFilter
{
    Any,
    Oem,
    Article,
    Analog
}

/// <summary>Фильтр наличия. Пустое значение эквивалентно «Все».</summary>
public enum AvailabilityFilter
{
    All,
    InStock,
    OutOfStock
}

/// <summary>
/// Условия расширенного поиска. Все поля необязательные:
/// пустые поля игнорируются, заполненные объединяются по логическому И.
/// </summary>
public class SearchCriteria
{
    public string? Name { get; set; }

    public string? Code { get; set; }

    public string? Number { get; set; }

    public NumberTypeFilter NumberFilter { get; set; } = NumberTypeFilter.Any;

    public string? Manufacturer { get; set; }

    public string? CarApplicability { get; set; }

    public AvailabilityFilter Availability { get; set; } = AvailabilityFilter.All;

    /// <summary>Поиск по физическим параметрам, мм. Пустое поле — условие не задано.</summary>
    public decimal? Diameter { get; set; }

    public decimal? Thickness { get; set; }

    public decimal? Width { get; set; }

    /// <summary>Допустимый разброс параметров в процентах от искомого значения (±).</summary>
    public decimal TolerancePercent { get; set; }
}
