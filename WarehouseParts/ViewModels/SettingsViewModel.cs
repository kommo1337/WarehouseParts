using System.Diagnostics;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarehouseParts.Services;

namespace WarehouseParts.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly BackupService _backup;

    [ObservableProperty]
    private string _databasePathText = "";

    [ObservableProperty]
    private string _versionText = "";

    public SettingsViewModel(BackupService backup)
    {
        _backup = backup;
        DatabasePathText = $"Файл базы данных: {_backup.DatabasePath}";
        VersionText = $"Версия приложения: {GetAppVersion()}";
    }

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        try
        {
            var path = await _backup.CreateBackupAsync();
            MessageBox.Show(
                $"Резервная копия создана:{Environment.NewLine}{path}",
                "Резервное копирование",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = System.IO.Path.GetDirectoryName(_backup.DatabasePath)!,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    private static string GetAppVersion()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
               ?? assembly.GetName().Version?.ToString()
               ?? "";
    }
}
