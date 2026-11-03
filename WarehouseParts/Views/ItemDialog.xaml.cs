using System.Windows;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Views;

public partial class ItemDialog : Window
{
    private readonly ItemDialogViewModel _viewModel;

    public ItemDialog(CatalogService catalog, CatalogItemRow? editRow)
    {
        InitializeComponent();
        _viewModel = new ItemDialogViewModel(catalog, editRow);
        DataContext = _viewModel;
        Title = editRow is null ? "Новая позиция" : "Изменение позиции";
    }

    /// <summary>Id созданной позиции, если диалог завершился сохранением новой позиции.</summary>
    public int? SavedItemId => _viewModel.SavedItemId;

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        var (ok, error) = await _viewModel.SaveAsync();
        if (ok)
        {
            DialogResult = true;
            return;
        }

        MessageBox.Show(error, "Проверка данных", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}
