using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarehouseParts.Services;
using WarehouseParts.Ui;

namespace WarehouseParts.ViewModels;

public partial class SettingsViewModel : ObservableObject
{
    private readonly BackupService _backup;
    private readonly UiSettingsService? _uiSettings;

    [ObservableProperty]
    private string _databasePathText = "";

    [ObservableProperty]
    private string _versionText = "";

    [ObservableProperty]
    private bool _isCreatingBackup;

    [ObservableProperty]
    private string _backupResultText = "";

    private string? _lastBackupPath;

    public SettingsViewModel(BackupService backup, UiSettingsService? uiSettings = null)
    {
        _backup = backup;
        _uiSettings = uiSettings;
        DatabasePathText = $"Файл базы данных: {_backup.DatabasePath}";
        VersionText = $"Версия приложения: {GetAppVersion()}";
    }

    public bool CanOpenBackupFolder => !string.IsNullOrEmpty(_lastBackupPath);

    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        IsCreatingBackup = true;
        try
        {
            var path = await _backup.CreateBackupAsync();
            _lastBackupPath = path;
            BackupResultText = $"Копия создана: {Path.GetFileName(path)} · {DateTime.Now:dd.MM.yyyy HH:mm}";
            OnPropertyChanged(nameof(CanOpenBackupFolder));
            OpenBackupFolderCommand.NotifyCanExecuteChanged();
            AppFeedback.Send("Резервная копия создана");
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
        finally
        {
            IsCreatingBackup = false;
        }
    }

    [RelayCommand]
    private void OpenDataFolder()
    {
        OpenFolder(Path.GetDirectoryName(_backup.DatabasePath));
    }

    [RelayCommand]
    private void ResetInterfaceSettings()
    {
        _uiSettings?.ResetColumns();
        AppFeedback.Send("Настройки таблиц сброшены. Изменения применятся при следующем открытии раздела.");
    }

    [RelayCommand(CanExecute = nameof(CanOpenBackupFolder))]
    private void OpenBackupFolder()
    {
        if (_lastBackupPath is not null)
        {
            OpenFolder(Path.GetDirectoryName(_lastBackupPath));
        }
    }

    private static void OpenFolder(string? path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
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
