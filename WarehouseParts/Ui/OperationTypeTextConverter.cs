using System.Globalization;
using System.Windows.Data;
using WarehouseParts.Data;

namespace WarehouseParts.Ui;

public class OperationTypeTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        => value is OperationType type ? Data.DisplayTexts.For(type) : "";

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
