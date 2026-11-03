namespace WarehouseParts.Services;

/// <summary>
/// Разбирает текст, вставленный пользователем как список номеров:
/// номера разделены переводами строк, запятыми или точками с запятой.
/// Сохраняет исходное написание каждого номера.
/// </summary>
public static class NumberListParser
{
    private static readonly char[] Separators = { '\r', '\n', ',', ';' };

    /// <summary>Возвращает исходные (нетримmed из середины, но обрезанные по краям) номера в порядке ввода.</summary>
    public static IReadOnlyList<string> Parse(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return Array.Empty<string>();
        }

        return text.Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(part => NumberNormalizer.Normalize(part).Length > 0)
            .ToList();
    }

    /// <summary>Истинно, если текст похож на список из нескольких номеров (есть разделитель и минимум два номера).</summary>
    public static bool LooksLikeList(string? text)
    {
        return Parse(text).Count > 1;
    }
}
