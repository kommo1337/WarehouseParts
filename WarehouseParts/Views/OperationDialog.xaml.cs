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
        _viewModel.FocusRequested += FocusField;
        Loaded += OperationDialog_OnLoaded;
        Closed += (_, _) =>
        {
            _viewModel.FocusRequested -= FocusField;
            _viewModel.CancelPending();
        };
    }

    public string? SavedMessage => _viewModel.SavedMessage;

    public Task CancelPendingAsync() => _viewModel.CancelPendingAsync();

    private void OperationDialog_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_viewModel.HasPresetItem)
        {
            QuantityBox.Focus();
            QuantityBox.SelectAll();
            return;
        }

        FilterBox.Focus();
        FilterBox.SelectAll();
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            var (ok, _, _) = await _viewModel.SaveAsync();
            if (ok)
            {
                DialogResult = true;
            }
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    private void FocusField(string field)
    {
        if (field == "QuantityBox")
        {
            QuantityBox.Focus();
            QuantityBox.SelectAll();
            return;
        }

        FilterBox.Focus();
        FilterBox.SelectAll();
    }

    private void OperationDialog_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter
            && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.Control)
        {
            e.Handled = true;
            Save_Click(sender, new RoutedEventArgs());
        }
    }

    private void Filter_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == System.Windows.Input.Key.Enter)
        {
            e.Handled = true;
            _viewModel.PickTopMatch();
            QuantityBox.Focus();
            QuantityBox.SelectAll();
        }
    }
}
