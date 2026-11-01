using System.IO;
using System.Windows;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static DbContextOptions<AppDbContext> DbOptions { get; private set; } = null!;

    public static string DataDirectory { get; private set; } = "";

    public static string BackupDirectory => Path.Combine(DataDirectory, "Backups");

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            LogError($"Необработанное исключение домена: {args.ExceptionObject}");

        DataDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WarehouseParts");
        Directory.CreateDirectory(DataDirectory);

        DbOptions = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite($"Data Source={Path.Combine(DataDirectory, "sklad.db")}")
            .Options;

        try
        {
            await using var db = new AppDbContext(DbOptions);
            await DatabaseInitializer.InitializeAsync(db);

            try
            {
                var backup = new BackupService(db, Path.Combine(DataDirectory, "sklad.db"), BackupDirectory);
                await backup.AutoBackupIfNeededAsync();
            }
            catch (Exception ex)
            {
                LogError($"Автоматическое резервное копирование: {ex}");
            }
        }
        catch (DatabaseCorruptedException ex)
        {
            MessageBox.Show(
                $"{ex.Message}{Environment.NewLine}{Environment.NewLine}Восстановите базу из резервной копии.{Environment.NewLine}Папка резервных копий: {BackupDirectory}",
                "База данных повреждена",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown();
            return;
        }

        new MainWindow().Show();
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError($"Необработанное исключение: {e.Exception}");
        MessageBox.Show(
            "Произошла непредвиденная ошибка. Технические детали записаны в errors.log.",
            "Ошибка",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
        e.Handled = true;
    }

    private static void LogError(string message)
    {
        try
        {
            File.AppendAllText(
                Path.Combine(DataDirectory, "errors.log"),
                $"{DateTime.Now:dd.MM.yyyy HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch
        {
            // Запись в лог не должна ронять приложение.
        }
    }
}
