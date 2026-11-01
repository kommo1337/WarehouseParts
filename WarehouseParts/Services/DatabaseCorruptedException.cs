namespace WarehouseParts.Services;

/// <summary>
/// Файл базы данных не прошел PRAGMA integrity_check.
/// </summary>
public class DatabaseCorruptedException : Exception
{
    public DatabaseCorruptedException(string detail)
        : base($"Файл базы данных поврежден. Проверка целостности вернула: {detail}")
    {
    }
}
