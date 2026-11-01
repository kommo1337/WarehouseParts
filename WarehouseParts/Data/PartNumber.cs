namespace WarehouseParts.Data;

/// <summary>Один номер одной запчасти (OEM, артикул или аналог).</summary>
public class PartNumber
{
    public int Id { get; set; }

    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    public NumberType NumberType { get; set; }

    /// <summary>Исходный номер, как введен пользователем. Показывается в интерфейсе.</summary>
    public string NumberRaw { get; set; } = "";

    /// <summary>Нормализованный номер (NumberNormalizer). Ключ сравнений и связей аналогов.</summary>
    public string NumberNormalized { get; set; } = "";

    public string? Comment { get; set; }
}
