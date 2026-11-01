namespace WarehouseParts.Data;

/// <summary>Отображаемые русские подписи значений перечислений.</summary>
public static class DisplayTexts
{
    public const string Receipt = "Приход";
    public const string Issue = "Расход";
    public const string Piece = "Шт";
    public const string Kit = "Компл";
    public const string Oem = "OEM";
    public const string Article = "Артикул";
    public const string Analog = "Аналог";

    public static readonly string[] Operations = { Receipt, Issue };
    public static readonly string[] Units = { Piece, Kit };
    public static readonly string[] NumberTypes = { Oem, Article, Analog };
    public static readonly string[] NumberTypeFilters = { "Любой", Oem, Article, Analog };
    public static readonly string[] AvailabilityFilters = { "Все", StockStatus.InStock, StockStatus.OutOfStock };

    public static string For(OperationType type) => type == OperationType.Receipt ? Receipt : Issue;

    public static string For(UnitType unit) => unit == UnitType.Piece ? Piece : Kit;

    public static string For(NumberType numberType)
        => numberType == NumberType.Oem ? Oem : (numberType == NumberType.Article ? Article : Analog);

    public static OperationType ParseOperation(string text) => text == Issue ? OperationType.Issue : OperationType.Receipt;

    public static UnitType ParseUnit(string text) => text == Kit ? UnitType.Kit : UnitType.Piece;

    public static NumberType ParseNumberType(string text)
        => text == Oem ? NumberType.Oem : (text == Article ? NumberType.Article : NumberType.Analog);
}
