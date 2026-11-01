using WarehouseParts.Data;

namespace WarehouseParts.Services;

/// <summary>Позиция, связанная общим номером с данной (для окна «Номера и аналоги позиции»).</summary>
public class RelatedItemRow
{
    public Item Item { get; set; } = null!;

    public int Stock { get; set; }

    public string Status => StockStatus.FromStock(Stock);

    /// <summary>Общий номер (исходный вид); несколько номеров объединены через «, » без дублей.</summary>
    public string CommonNumbers { get; set; } = "";
}
