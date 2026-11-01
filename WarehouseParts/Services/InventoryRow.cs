namespace WarehouseParts.Services;

public enum InventoryAction
{
    /// <summary>Фактический остаток не заполнен — кнопка недоступна.</summary>
    None,

    /// <summary>Расхождение больше нуля — «Приход +N».</summary>
    Receipt,

    /// <summary>Расхождение меньше нуля — «Расход −N».</summary>
    Issue,

    /// <summary>Расхождение равно нулю — «Без изменений».</summary>
    NoChange
}

/// <summary>
/// Строка инвентаризации. Фактический остаток держится только в памяти текущего
/// сеанса и не сохраняется между запусками приложения.
/// </summary>
public class InventoryRow
{
    public int ItemId { get; set; }

    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Учетный остаток из журнала операций.</summary>
    public int Stock { get; set; }

    /// <summary>Фактический остаток, введенный пользователем. Null — не заполнен.</summary>
    public int? Fact { get; set; }

    /// <summary>Расхождение = фактический − учетный. Пустое, если факт не заполнен.</summary>
    public int? Discrepancy => Fact.HasValue ? Fact.Value - Stock : null;

    public InventoryAction Action
    {
        get
        {
            if (!Discrepancy.HasValue)
            {
                return InventoryAction.None;
            }

            if (Discrepancy.Value > 0)
            {
                return InventoryAction.Receipt;
            }

            return Discrepancy.Value < 0 ? InventoryAction.Issue : InventoryAction.NoChange;
        }
    }

    /// <summary>Динамическая подпись кнопки действия строки.</summary>
    public string ActionLabel
    {
        get
        {
            if (!Discrepancy.HasValue)
            {
                return "—";
            }

            if (Discrepancy.Value > 0)
            {
                return $"Приход +{Discrepancy.Value}";
            }

            return Discrepancy.Value < 0
                ? $"Расход −{Math.Abs(Discrepancy.Value)}"
                : "Без изменений";
        }
    }
}
