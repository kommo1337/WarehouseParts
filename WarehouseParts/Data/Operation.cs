namespace WarehouseParts.Data;

/// <summary>Проведенная операция журнала. Таблица хранит только корректные операции.</summary>
public class Operation
{
    public int Id { get; set; }

    public OperationType OperationType { get; set; }

    public int ItemId { get; set; }

    public Item Item { get; set; } = null!;

    /// <summary>Целое число больше нуля.</summary>
    public int Quantity { get; set; }

    /// <summary>Дата и время создания операции. Проставляется программой.</summary>
    public DateTime CreatedAt { get; set; }

    public string? Comment { get; set; }
}
