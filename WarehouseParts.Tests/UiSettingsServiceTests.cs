using WarehouseParts.Services;
using WarehouseParts.Ui;

namespace WarehouseParts.Tests;

public class UiSettingsServiceTests : IDisposable
{
    private readonly string _dir;

    public UiSettingsServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "wp-ui-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose() => Directory.Delete(_dir, recursive: true);

    [Fact]
    public void Столбцы_СохраняютсяИВосстанавливаются()
    {
        var settings = new UiSettingsService(_dir);
        settings.SaveColumns("catalog", new List<TableColumnState>
        {
            new() { Key = "Наименование", Visible = true, Width = 250, Order = 0 },
            new() { Key = "Остаток", Visible = false, Width = -1, Order = 1 },
        });

        var reloaded = new UiSettingsService(_dir);
        var columns = reloaded.GetColumns("catalog");

        Assert.NotNull(columns);
        Assert.Equal(2, columns.Count);
        Assert.Equal("Наименование", columns[0].Key);
        Assert.False(columns[1].Visible);
        Assert.Equal(250, columns[0].Width);
        Assert.Null(reloaded.GetColumns("journal"));
    }

    [Fact]
    public void ПоврежденныйФайл_Игнорируется()
    {
        File.WriteAllText(Path.Combine(_dir, "ui-settings.json"), "{ не json");

        var settings = new UiSettingsService(_dir);

        Assert.True(settings.PreviewVisible);
        Assert.Null(settings.GetColumns("catalog"));
    }

    [Fact]
    public void СбросСтолбцов_ОчищаетВсеТаблицы()
    {
        var settings = new UiSettingsService(_dir);
        settings.SaveColumns("catalog", new List<TableColumnState> { new() { Key = "Код" } });
        settings.SaveColumns("journal", new List<TableColumnState> { new() { Key = "Дата" } });

        settings.ResetColumns();

        Assert.Null(settings.GetColumns("catalog"));
        Assert.Null(settings.GetColumns("journal"));

        var reloaded = new UiSettingsService(_dir);
        Assert.Null(reloaded.GetColumns("catalog"));
    }
}
