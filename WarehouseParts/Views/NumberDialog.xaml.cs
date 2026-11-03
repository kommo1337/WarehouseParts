using System.Windows;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Views;

public partial class NumberDialog : Window
{
    private readonly NumberDialogViewModel _viewModel;

    public NumberDialog(CatalogService catalog, int itemId, PartNumber? editNumber)
    {
        InitializeComponent();
        _viewModel = new NumberDialogViewModel(catalog, itemId, editNumber);
        DataContext = _viewModel;
        Title = editNumber is null ? "Новый номер" : "Изменение номера";
    }

    private void NumberDialog_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter
            && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            e.Handled = true;
            Save_Click(sender, new RoutedEventArgs());
        }
    }

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
