namespace WarehouseParts.Data;

/// <summary>Тип операции журнала. В БД хранится русской строкой: Приход / Расход.</summary>
public enum OperationType
{
    Receipt,
    Issue
}

/// <summary>Единица измерения. В БД хранится русской строкой: Шт / Компл.</summary>
public enum UnitType
{
    Piece,
    Kit
}

/// <summary>Тип номера запчасти. В БД хранится русской строкой: OEM / Артикул / Аналог.</summary>
public enum NumberType
{
    Oem,
    Article,
    Analog
}
