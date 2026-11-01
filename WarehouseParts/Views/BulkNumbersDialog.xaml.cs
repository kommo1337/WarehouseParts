using System.Windows;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Views;

public partial class BulkNumbersDialog : Window
{
    private readonly CatalogService _catalog;
    private readonly int _itemId;

    public BulkNumbersDialog(CatalogService catalog, int itemId)
    {
        InitializeComponent();
        _catalog = catalog;
        _itemId = itemId;
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var type = DisplayTexts.ParseNumberType((string)TypeCombo.SelectedItem);
            var result = await _catalog.BulkAddNumbersAsync(_itemId, type, NumbersText.Text);
            MessageBox.Show(
                $"Добавлено: {result.Added}, пропущено дубликатов: {result.SkippedDuplicates}",
                "Массовый ввод",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
            DialogResult = true;
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }
}
