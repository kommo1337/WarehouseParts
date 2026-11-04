using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using WarehouseParts.Services;

namespace WarehouseParts.Ui;

/// <summary>
/// Настраиваемые столбцы DataGrid: восстановление и сохранение видимости,
/// ширины и порядка в UiSettingsService, меню «Столбцы», перемещение и сброс.
/// </summary>
public static class DataGridColumns
{
    /// <summary>Задаётся при запуске приложения; в тестах может быть null (тогда поведение неактивно).</summary>
    public static UiSettingsService? Settings { get; set; }

    public static readonly DependencyProperty TableKeyProperty =
        DependencyProperty.RegisterAttached(
            "TableKey",
            typeof(string),
            typeof(DataGridColumns),
            new PropertyMetadata(string.Empty, OnTableKeyChanged));

    public static readonly DependencyProperty RequiredColumnsProperty =
        DependencyProperty.RegisterAttached(
            "RequiredColumns",
            typeof(string),
            typeof(DataGridColumns),
            new PropertyMetadata(string.Empty));

    public static string GetTableKey(DataGrid grid) => (string)grid.GetValue(TableKeyProperty);

    public static void SetTableKey(DataGrid grid, string value) => grid.SetValue(TableKeyProperty, value);

    public static string GetRequiredColumns(DataGrid grid) => (string)grid.GetValue(RequiredColumnsProperty);

    public static void SetRequiredColumns(DataGrid grid, string value) => grid.SetValue(RequiredColumnsProperty, value);

    private static void OnTableKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not DataGrid grid || string.IsNullOrEmpty((string)e.NewValue))
        {
            return;
        }

        // Таблица без визуального дерева ещё не готова: восстановление выполнится по Loaded.
        if (grid.IsLoaded)
        {
            RestoreOnLoaded(grid);
        }
        else
        {
            grid.Loaded += (_, _) => RestoreOnLoaded(grid);
        }

        grid.ColumnDisplayIndexChanged += (_, _) => Save(grid);
        grid.Unloaded += (_, _) => Save(grid);
    }

    private static void RestoreOnLoaded(DataGrid grid)
    {
        var state = _states.GetOrCreateValue(grid);
        if (state.Initialized)
        {
            return;
        }

        Restore(grid);
        state.Initialized = true;
    }

    /// <summary>Применяет сохранённые настройки: порядок (DisplayIndex), видимость и ширину.</summary>
    public static void Restore(DataGrid grid)
    {
        var settings = Settings;
        var tableKey = GetTableKey(grid);
        if (settings is null || tableKey.Length == 0)
        {
            return;
        }

        var saved = settings.GetColumns(tableKey);
        if (saved is null)
        {
            return;
        }

        var order = 0;
        foreach (var state in saved)
        {
            var column = grid.Columns.FirstOrDefault(c => string.Equals(HeaderText(c), state.Key, StringComparison.Ordinal));
            if (column is null)
            {
                continue;
            }

            column.Visibility = state.Visible ? Visibility.Visible : Visibility.Collapsed;
            if (state.Width > 0)
            {
                column.Width = new DataGridLength(state.Width);
            }

            var targetIndex = Math.Min(order, grid.Columns.Count - 1);
            if (column.DisplayIndex != targetIndex)
            {
                column.DisplayIndex = targetIndex;
            }

            order++;
        }
    }

    public static void Save(DataGrid grid)
    {
        var settings = Settings;
        var tableKey = GetTableKey(grid);
        if (settings is null || tableKey.Length == 0
            || !_states.TryGetValue(grid, out var state) || !state.Initialized)
        {
            return;
        }

        var columns = grid.Columns
            .OrderBy(c => c.DisplayIndex)
            .Select(c => new TableColumnState
            {
                Key = HeaderText(c),
                Visible = c.Visibility == Visibility.Visible,
                Width = c.Width.IsAbsolute ? c.Width.Value : -1,
                Order = c.DisplayIndex,
            })
            .ToList();
        settings.SaveColumns(tableKey, columns);
    }

    private static readonly ConditionalWeakTable<DataGrid, GridState> _states = new();

    private sealed class GridState
    {
        public bool Initialized { get; set; }
    }

    public static string HeaderText(DataGridColumn column) => column.Header as string ?? string.Empty;

    public static bool IsRequired(DataGrid grid, DataGridColumn column)
    {
        var required = GetRequiredColumns(grid);
        return required.Length > 0
               && required.Split(',').Any(r => string.Equals(r.Trim(), HeaderText(column), StringComparison.Ordinal));
    }

    public static void SetVisible(DataGrid grid, DataGridColumn column, bool visible)
    {
        column.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        Save(grid);
    }

    /// <summary>Сдвигает столбец на одну позицию влево (-1) или вправо (+1) среди видимых.</summary>
    public static void Move(DataGrid grid, DataGridColumn? column, int delta)
    {
        column ??= TargetColumn(grid);
        if (column is null)
        {
            return;
        }

        var visible = grid.Columns
            .Where(c => c.Visibility == Visibility.Visible)
            .OrderBy(c => c.DisplayIndex)
            .ToList();
        var index = visible.IndexOf(column);
        var target = index + delta;
        if (index < 0 || target < 0 || target >= visible.Count)
        {
            return;
        }

        (column.DisplayIndex, visible[target].DisplayIndex) = (visible[target].DisplayIndex, column.DisplayIndex);
        Save(grid);
    }

    public static void AutoSize(DataGrid grid, DataGridColumn? column)
    {
        column ??= TargetColumn(grid);
        if (column is null)
        {
            return;
        }

        column.Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
        Save(grid);
    }

    /// <summary>Сбрасывает настройки таблицы: все столбцы видимы, автоширина, исходный порядок.</summary>
    public static void Reset(DataGrid grid)
    {
        Settings?.ResetColumns();

        for (var i = 0; i < grid.Columns.Count; i++)
        {
            grid.Columns[i].Visibility = Visibility.Visible;
            grid.Columns[i].Width = new DataGridLength(1, DataGridLengthUnitType.Auto);
            grid.Columns[i].DisplayIndex = i;
        }
    }

    private static DataGridColumn? TargetColumn(DataGrid grid)
        => grid.CurrentColumn ?? grid.Columns.FirstOrDefault(c => c.Visibility == Visibility.Visible);

    /// <summary>Контекстное меню «Столбцы»: видимость, перемещение, автоширина, сброс.</summary>
    public static void OpenMenu(DataGrid grid, UIElement placementTarget)
    {
        var menu = new ContextMenu();
        var header = new MenuItem { Header = "Столбцы", IsEnabled = false, FontWeight = FontWeights.SemiBold };
        menu.Items.Add(header);

        foreach (var column in grid.Columns.OrderBy(c => c.DisplayIndex))
        {
            var col = column;
            var item = new MenuItem
            {
                Header = HeaderText(col),
                IsCheckable = true,
                IsChecked = col.Visibility == Visibility.Visible,
                StaysOpenOnClick = true,
                IsEnabled = !IsRequired(grid, col),
                ToolTip = IsRequired(grid, col) ? "Обязательный столбец нельзя скрыть" : "Показать или скрыть столбец",
            };
            item.Click += (_, _) => SetVisible(grid, col, item.IsChecked);
            menu.Items.Add(item);
        }

        menu.Items.Add(new Separator());

        var target = TargetColumn(grid);
        var moveLeft = new MenuItem { Header = "Переместить влево", ToolTip = "Сдвинуть текущий столбец влево" };
        moveLeft.Click += (_, _) => Move(grid, target, -1);
        var moveRight = new MenuItem { Header = "Переместить вправо", ToolTip = "Сдвинуть текущий столбец вправо" };
        moveRight.Click += (_, _) => Move(grid, target, 1);
        var autoSize = new MenuItem { Header = "Автоширина", ToolTip = "Подобрать ширину по содержимому" };
        autoSize.Click += (_, _) => AutoSize(grid, target);
        var reset = new MenuItem { Header = "Сбросить настройки таблицы", ToolTip = "Вернуть исходные столбцы" };
        reset.Click += (_, _) => Reset(grid);

        menu.Items.Add(moveLeft);
        menu.Items.Add(moveRight);
        menu.Items.Add(autoSize);
        menu.Items.Add(new Separator());
        menu.Items.Add(reset);

        menu.PlacementTarget = placementTarget;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }
}
