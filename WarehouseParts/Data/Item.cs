namespace WarehouseParts.Data;

/// <summary>Позиция каталога: одна уникальная запчасть.</summary>
public class Item
{
    public int Id { get; set; }

    /// <summary>Номер запчасти. Уникален, сравнение без учета регистра.</summary>
    public string Code { get; set; } = "";

    public string Name { get; set; } = "";

    public string? Manufacturer { get; set; }

    public UnitType Unit { get; set; }

    /// <summary>Применимость к автомобилю. Свободный текст, может быть пустым.</summary>
    public string? CarApplicability { get; set; }

    public string? Comment { get; set; }

    /// <summary>Диаметр, мм. Необязательный физический параметр.</summary>
    public decimal? Diameter { get; set; }

    /// <summary>Толщина, мм. Необязательный физический параметр.</summary>
    public decimal? Thickness { get; set; }

    /// <summary>Ширина, мм. Необязательный физический параметр.</summary>
    public decimal? Width { get; set; }

    public List<PartNumber> PartNumbers { get; set; } = new();

    public List<Operation> Operations { get; set; } = new();
}
