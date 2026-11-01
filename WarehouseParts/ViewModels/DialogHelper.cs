using System.Windows;
using WarehouseParts.Services;

namespace WarehouseParts.ViewModels;

/// <summary>Простые диалоги сообщений и подтверждений.</summary>
public static class DialogHelper
{
    public static void ShowError(Exception ex)
    {
        if (ex is DomainValidationException)
        {
            MessageBox.Show(ex.Message, "Проверка данных", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        ErrorLog.Write(ex.ToString());
        MessageBox.Show(
            "Произошла ошибка. Подробности записаны в errors.log.",
            "Ошибка",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }

    public static bool Confirm(string message)
        => MessageBox.Show(message, "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Question)
           == MessageBoxResult.Yes;
}
