namespace WarehouseParts.Services;

/// <summary>
/// Ожидаемая ошибка бизнес-правила. Сообщение показывается пользователю как есть;
/// в журнал данных операция не записывается.
/// </summary>
public class DomainValidationException : Exception
{
    public DomainValidationException(string message)
        : base(message)
    {
    }
}
