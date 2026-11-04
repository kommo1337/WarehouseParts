using System.Globalization;
using System.Windows.Data;
using WarehouseParts.Data;

namespace WarehouseParts.Ui;

/// <summary>Количество операции со знаком: «+N» для прихода и «−N» для расхода.</summary>
public class OperationQuantityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not Operation operation)
        {
            return "";
        }

        var sign = operation.OperationType == OperationType.Receipt ? "+" : "−";
        return $"{sign}{operation.Quantity}";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
