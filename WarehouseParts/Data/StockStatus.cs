namespace WarehouseParts.Data;

/// <summary>
/// Статус наличия позиции. Всегда вычисляется из остатка, в БД не хранится.
/// Остаток = 0 → «Нет в наличии», иначе → «В наличии».
/// </summary>
public static class StockStatus
{
    public const string InStock = "В наличии";
    public const string OutOfStock = "Нет в наличии";

    public static string FromStock(int stock) => stock > 0 ? InStock : OutOfStock;
}
