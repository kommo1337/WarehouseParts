using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.Ui;

namespace WarehouseParts.Tests;

public class GridCopyTests
{
    private sealed class Nested
    {
        public string? Code { get; set; }
    }

    private sealed class Row
    {
        public Nested? Item { get; set; }

        public int Stock { get; set; }
    }

    [Fact]
    public void ResolvePath_РазрешаетВложенныеСвойства()
    {
        var row = new Row { Item = new Nested { Code = "P-100" }, Stock = 7 };

        Assert.Equal("P-100", GridCopy.ResolvePath(row, "Item.Code"));
        Assert.Equal(7, GridCopy.ResolvePath(row, "Stock"));
        Assert.Null(GridCopy.ResolvePath(row, "Item.Missing"));
        Assert.Null(GridCopy.ResolvePath(new Row(), "Item.Code"));
    }

    [Fact]
    public void FormatCell_ПустыеИДаты()
    {
        Assert.Equal(string.Empty, GridCopy.FormatCell(null));
        Assert.Equal("abc", GridCopy.FormatCell("abc"));
        var date = new DateTime(2024, 3, 5, 9, 30, 0);
        Assert.Equal("05.03.2024 09:30", GridCopy.FormatCell(date));
    }

    [Fact]
    public void BuildRowsText_ФормируетTsvСЗаголовками()
    {
        var headers = new[] { "Код", "Остаток", "Комментарий" };
        var rows = new IReadOnlyList<object?>[]
        {
            new object?[] { "P-1", 5, null },
            new object?[] { "P-2", 0, "дефект" },
        };

        var text = GridCopy.BuildRowsText(headers, rows, withHeaders: true);
        var lines = text.Split(Environment.NewLine);

        Assert.Equal("Код\tОстаток\tКомментарий", lines[0]);
        Assert.Equal("P-1\t5\t", lines[1]);
        Assert.Equal("P-2\t0\tдефект", lines[2]);
    }

    [Fact]
    public void BuildRowsText_БезЗаголовков()
    {
        var text = GridCopy.BuildRowsText(
            new[] { "Код" },
            new IReadOnlyList<object?>[] { new object?[] { "P-1" } },
            withHeaders: false);

        Assert.Equal("P-1", text);
    }

    [Fact]
    public void ResolveColumnValue_ЧитаетПоПривязкеКолонки()
    {
        var row = new CatalogItemRow { Item = new Item { Code = "BRK-01", Name = "Колодки" }, Stock = 3 };
        var column = new System.Windows.Controls.DataGridTextColumn
        {
            Binding = new System.Windows.Data.Binding("Item.Code"),
        };

        Assert.Equal("BRK-01", GridCopy.ResolveColumnValue(row, column));
    }
}
