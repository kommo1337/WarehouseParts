using System.Text;

namespace WarehouseParts.Services;

/// <summary>
/// Нормализация номера запчасти для сравнений и связей аналогов:
/// остаются только буквы и цифры, буквы в верхнем регистре.
/// Примеры: "06a-115 561b" -> "06A115561B", "W 712/52" -> "W71252".
/// </summary>
public static class NumberNormalizer
{
    public static string Normalize(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return "";
        }

        var builder = new StringBuilder(raw.Length);
        foreach (var c in raw)
        {
            if (char.IsLetterOrDigit(c))
            {
                builder.Append(char.ToUpperInvariant(c));
            }
        }

        return builder.ToString();
    }
}
