using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using WarehouseParts.Services;
using WarehouseParts.Ui;

namespace WarehouseParts.Tests;

public class InfrastructureStage3Tests : IDisposable
{
    private readonly string _settingsDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));

    public InfrastructureStage3Tests()
    {
        Directory.CreateDirectory(_settingsDir);
    }

    public void Dispose()
    {
        DataGridColumns.Settings = null;
        try
        {
            Directory.Delete(_settingsDir, recursive: true);
        }
        catch
        {
            // Временный каталог настроек не должен ронять тестовый процесс.
        }
    }

    [Fact]
    public void EscapeField_СпецсимволыОборачиваютсяВКавычки()
    {
        Assert.Equal("обычный", GridCopy.EscapeField("обычный"));
        Assert.Equal("\"с\tтабуляцией\"", GridCopy.EscapeField("с\tтабуляцией"));
        Assert.Equal("\"с\nпереводом\"", GridCopy.EscapeField("с\nпереводом"));
        Assert.Equal("\"двойная \"\"кавычка\"\"\"", GridCopy.EscapeField("двойная \"кавычка\""));
    }

    [Fact]
    public void BuildRowsText_СохраняетСтруктуруTsvПриСпецсимволах()
    {
        var headers = new[] { "Код", "Комментарий" };
        var rows = new IReadOnlyList<object?>[]
        {
            new object?[] { "P-1", "строка\tс табом" },
            new object?[] { "P-2", "многострочный\nкомментарий" },
        };

        var text = GridCopy.BuildRowsText(headers, rows, withHeaders: true);
        var lines = text.Split(Environment.NewLine);

        // Каждая логическая строка — отдельная физическая строка, даже со спецсимволами внутри значений.
        Assert.Equal(3, lines.Length);
        Assert.Equal("Код\tКомментарий", lines[0]);
        Assert.StartsWith("P-1\t\"строка", lines[1]);
        Assert.EndsWith("с табом\"", lines[1]);
        Assert.StartsWith("P-2\t\"многострочный\nкомментарий\"", lines[2]);
    }

    [Fact]
    public void Restore_ВыполняетсяТолькоПослеLoaded()
    {
        RunInSta(() =>
        {
            var settings = new UiSettingsService(_settingsDir);
            settings.SaveColumns("test", new List<TableColumnState>
            {
                new() { Key = "Скрытый", Visible = false, Width = -1, Order = 0 },
                new() { Key = "Видимый", Visible = true, Width = -1, Order = 1 },
            });
            DataGridColumns.Settings = settings;

            var grid = CreateGridFromXaml();
            var hidden = grid.Columns.First(c => (string)c.Header == "Скрытый");
            DataGridColumns.SetTableKey(grid, "test");

            // Loaded ещё не было — столбцы в исходном состоянии.
            Assert.Equal(Visibility.Visible, hidden.Visibility);

            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            Assert.Equal(Visibility.Collapsed, hidden.Visibility);
            DataGridColumns.Save(grid);
            Assert.NotNull(settings.GetColumns("test"));
        });
    }

    [Fact]
    public void Restore_ИзРеальногоXaml_ПрименяетПорядокИШирину()
    {
        RunInSta(() =>
        {
            var settings = new UiSettingsService(_settingsDir);
            settings.SaveColumns("test", new List<TableColumnState>
            {
                new() { Key = "Второй", Visible = true, Width = 123, Order = 0 },
                new() { Key = "Первый", Visible = true, Width = -1, Order = 1 },
            });
            DataGridColumns.Settings = settings;

            var grid = CreateGridFromXaml();
            DataGridColumns.SetTableKey(grid, "test");
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));

            var first = grid.Columns.First(c => (string)c.Header == "Первый");
            var second = grid.Columns.First(c => (string)c.Header == "Второй");
            Assert.Equal(1, first.DisplayIndex);
            Assert.Equal(0, second.DisplayIndex);
            Assert.True(Math.Abs(second.Width.Value - 123) < 0.01);
        });
    }

    [Fact]
    public void ЗакрытаяТаблица_ОсвобождаетсяСборщикомМусора()
    {
        var weak = RunInSta(() =>
        {
            DataGridColumns.Settings = new UiSettingsService(_settingsDir);
            var grid = CreateGridFromXaml();
            DataGridColumns.SetTableKey(grid, "test");
            grid.RaiseEvent(new RoutedEventArgs(FrameworkElement.LoadedEvent));
            var reference = new WeakReference(grid);
            // ReSharper disable once RedundantAssignment — сильная ссылка должна исчезнуть до GC.
            grid = null!;
            return reference;
        });

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(weak.IsAlive, "ConditionalWeakTable не должна удерживать выгруженную таблицу");
    }

    private static DataGrid CreateGridFromXaml()
    {
        const string xaml = """
            <DataGrid xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                      xmlns:ui="clr-namespace:WarehouseParts.Ui;assembly=WarehouseParts"
                      ui:DataGridColumns.TableKey="test" ui:DataGridColumns.RequiredColumns="Первый">
                <DataGrid.Columns>
                    <DataGridTextColumn Header="Первый" Binding="{Binding Code}"/>
                    <DataGridTextColumn Header="Второй" Binding="{Binding Name}"/>
                    <DataGridTextColumn Header="Скрытый" Binding="{Binding Stock}"/>
                </DataGrid.Columns>
            </DataGrid>
            """;
        return (DataGrid)XamlReader.Parse(xaml);
    }

    private static T RunInSta<T>(Func<T> action)
    {
        T? result = default;
        Exception? error = null;
        var thread = new Thread(() =>
        {
            try
            {
                result = action();
            }
            catch (Exception ex)
            {
                error = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        return error is null ? result! : throw error;
    }

    private static void RunInSta(Action action) => RunInSta(() =>
    {
        action();
        return true;
    });
}
