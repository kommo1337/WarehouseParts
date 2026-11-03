using System.IO;
using System.Text.Json;

namespace WarehouseParts.Services;

/// <summary>
/// Локальные UI-настройки пользователя (JSON в каталоге данных приложения).
/// Повреждение или отсутствие файла не мешает запуску: используются значения по умолчанию.
/// </summary>
public class UiSettingsService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    private readonly string _filePath;
    private UiSettings _settings = new();

    public UiSettingsService(string dataDirectory)
    {
        _filePath = Path.Combine(dataDirectory, "ui-settings.json");
        Load();
    }

    public bool PreviewVisible
    {
        get => _settings.PreviewVisible;
        set
        {
            if (_settings.PreviewVisible == value)
            {
                return;
            }

            _settings.PreviewVisible = value;
            Save();
        }
    }

    /// <summary>Настройки столбцов таблицы (порядок соответствует DisplayIndex).</summary>
    public IReadOnlyList<TableColumnState>? GetColumns(string tableKey)
        => _settings.Tables.TryGetValue(tableKey, out var columns) ? columns : null;

    public void SaveColumns(string tableKey, List<TableColumnState> columns)
    {
        _settings.Tables[tableKey] = columns;
        Save();
    }

    /// <summary>Сброс настроек всех таблиц (видимость, ширина и порядок столбцов).</summary>
    public void ResetColumns()
    {
        if (_settings.Tables.Count == 0)
        {
            return;
        }

        _settings.Tables.Clear();
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return;
            }

            var json = File.ReadAllText(_filePath);
            var loaded = JsonSerializer.Deserialize<UiSettings>(json);
            if (loaded is not null)
            {
                _settings = loaded;
            }
        }
        catch
        {
            _settings = new UiSettings();
        }
    }

    private void Save()
    {
        try
        {
            File.WriteAllText(_filePath, JsonSerializer.Serialize(_settings, JsonOptions));
        }
        catch
        {
            // Настройки интерфейса не должны прерывать работу приложения.
        }
    }

    private sealed class UiSettings
    {
        public bool PreviewVisible { get; set; } = true;

        public Dictionary<string, List<TableColumnState>> Tables { get; set; } = new();
    }
}

/// <summary>Сохранённое состояние одного столбца таблицы.</summary>
public sealed class TableColumnState
{
    /// <summary>Заголовок столбца — стабильный ключ в рамках таблицы.</summary>
    public string Key { get; set; } = string.Empty;

    public bool Visible { get; set; } = true;

    /// <summary>Ширина в px; -1 — не задана (авто).</summary>
    public double Width { get; set; } = -1;

    public int Order { get; set; }
}
