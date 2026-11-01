using System.Windows;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Views;

public partial class OperationDialog : Window
{
    private readonly OperationDialogViewModel _viewModel;

    public OperationDialog(OperationsService operations, CatalogService catalog, OperationType? presetType, Item? presetItem)
    {
        InitializeComponent();
        _viewModel = new OperationDialogViewModel(operations, catalog, presetType, presetItem);
        DataContext = _viewModel;
        Closed += (_, _) => _viewModel.CancelPending();
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

    private void Filter_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            _viewModel.PickTopMatch();
            QuantityBox.Focus();
        }
    }
}
