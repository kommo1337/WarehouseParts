using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Ui;

/// <summary>
/// Копирование из DataGrid одним действием:
/// Ctrl+C — текущая ячейка, Ctrl+Shift+C — вся строка (заголовки и значения, TSV).
/// Копируются исходные отображаемые значения, не нормализованные.
/// </summary>
public static class GridCopy
{
    /// <summary>Обрабатывает Ctrl+C / Ctrl+Shift+C для таблицы. Возвращает true, если клавиша обработана.</summary>
    public static bool HandleKeyDown(DataGrid grid, KeyEventArgs e)
    {
        // В редактируемых полях (фактический остаток и т.п.) Ctrl+C работает как обычно — копирование текста.
        if (e.OriginalSource is TextBox or System.Windows.Controls.Primitives.TextBoxBase or PasswordBox)
        {
            return false;
        }

        var modifiers = Keyboard.Modifiers;
        if (e.Key != Key.C || modifiers is not (ModifierKeys.Control or (ModifierKeys.Control | ModifierKeys.Shift)))
        {
            return false;
        }

        if (grid.SelectedItem is null)
        {
            return false;
        }

        var copied = modifiers == ModifierKeys.Control
            ? CopyCurrentCell(grid)
            : CopySelectedRow(grid);
        e.Handled = copied;
        return copied;
    }

    /// <summary>Копирует значение текущей ячейки (колонки, на которой стоит выбор).</summary>
    public static bool CopyCurrentCell(DataGrid grid)
    {
        var item = grid.SelectedItem;
        var column = grid.CurrentColumn ?? grid.Columns.FirstOrDefault(c => c.Visibility == Visibility.Visible);
        if (item is null || column is null)
        {
            return false;
        }

        var value = ResolveColumnValue(item, column);
        if (value is null)
        {
            // Шаблонная колонка без привязки — копируем строку целиком.
            return CopySelectedRow(grid);
        }

        CopyToClipboard(FormatCell(value), $"Скопировано: {FormatCell(value)}");
        return true;
    }

    /// <summary>Копирует видимые поля выбранной строки с заголовками в формате TSV.</summary>
    public static bool CopySelectedRow(DataGrid grid)
    {
        var item = grid.SelectedItem;
        if (item is null)
        {
            return false;
        }

        var headers = new List<string>();
        var values = new List<object?>();
        foreach (var column in grid.Columns.OrderBy(c => c.DisplayIndex))
        {
            if (column.Visibility != Visibility.Visible)
            {
                continue;
            }

            headers.Add(DataGridColumns.HeaderText(column));
            values.Add(ResolveColumnValue(item, column));
        }

        if (headers.Count == 0)
        {
            return false;
        }

        var text = BuildRowsText(headers, new[] { values }, withHeaders: false);
        var title = values.Count > 0 ? FormatCell(values[0]) : "строка";
        CopyToClipboard(text, $"Строка «{title}» скопирована");
        return true;
    }

    /// <summary>Читает значение ячейки по пути привязки колонки; null — если привязки нет.</summary>
    public static object? ResolveColumnValue(object item, DataGridColumn column)
    {
        var path = column is DataGridBoundColumn bound && bound.Binding is Binding binding
            ? binding.Path.Path
            : null;
        return path is null ? null : ResolvePath(item, path);
    }

    /// <summary>Разрешает простой путь вида "Item.Code" через цепочку свойств.</summary>
    public static object? ResolvePath(object? source, string path)
    {
        object? current = source;
        foreach (var part in path.Split('.'))
        {
            if (current is null)
            {
                return null;
            }

            current = current.GetType().GetProperty(part)?.GetValue(current);
        }

        return current;
    }

    public static string FormatCell(object? value) => value switch
    {
        null => string.Empty,
        DateTime date => date.ToString("dd.MM.yyyy HH:mm"),
        _ => value.ToString() ?? string.Empty,
    };

    /// <summary>Формирует TSV-текст: при withHeaders первая строка — заголовки.</summary>
    public static string BuildRowsText(
        IReadOnlyList<string> headers,
        IReadOnlyList<IReadOnlyList<object?>> rows,
        bool withHeaders)
    {
        var lines = new List<string>();
        if (withHeaders)
        {
            lines.Add(string.Join("\t", headers));
        }

        foreach (var row in rows)
        {
            lines.Add(string.Join("\t", row.Select(FormatCell)));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>Копирует текст в буфер и показывает toast-уведомление.</summary>
    public static void CopyWithFeedback(string text, string feedback)
    {
        try
        {
            Clipboard.SetText(text);
            AppFeedback.Send(feedback);
        }
        catch (Exception ex)
        {
            ViewModels.DialogHelper.ShowError(ex);
        }
    }

    private static void CopyToClipboard(string text, string feedback) => CopyWithFeedback(text, feedback);
}
