using System.IO;

namespace WarehouseParts.Services;

/// <summary>
/// Технический лог в файл errors.log в папке данных.
/// Запись в лог никогда не должна ронять приложение.
/// </summary>
public static class ErrorLog
{
    private static string? _path;

    public static string Path
    {
        get => _path ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WarehouseParts",
            "errors.log");
        set => _path = value;
    }

    public static void Write(string message)
    {
        try
        {
            File.AppendAllText(
                Path,
                $"{DateTime.Now:dd.MM.yyyy HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // Запись в лог не должна ронять приложение.
        }
    }
}
