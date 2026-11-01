using WarehouseParts.Data;

namespace WarehouseParts.Services;

/// <summary>Строка журнала операций с данными позиции и расчетным остатком после операции.</summary>
public class JournalRow
{
    public JournalRow(Operation operation, int balanceAfter)
    {
        Operation = operation;
        BalanceAfter = balanceAfter;
    }

    public Operation Operation { get; }

    public int Id => Operation.Id;

    public DateTime CreatedAt => Operation.CreatedAt;

    public OperationType OperationType => Operation.OperationType;

    public string OperationTypeDisplay => DisplayTexts.For(OperationType);

    public string ItemCode => Operation.Item.Code;

    public string ItemName => Operation.Item.Name;

    public string UnitDisplay => DisplayTexts.For(Operation.Item.Unit);

    public int Quantity => Operation.Quantity;

    /// <summary>Остаток позиции после этой операции. Вычисляется, не хранится.</summary>
    public int BalanceAfter { get; }

    public string? Comment => Operation.Comment;
}
