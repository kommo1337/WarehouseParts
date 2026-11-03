using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WarehouseParts.Services;
using WarehouseParts.Ui;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Views;

public partial class ItemDialog : Window
{
    private readonly ItemDialogViewModel _viewModel;
    private bool _saveWithoutOemConfirmed;

    public ItemDialog(CatalogService catalog, OperationsService operations, CatalogItemRow? editRow, IReadOnlyList<string>? initialNumbers = null)
    {
        InitializeComponent();
        _viewModel = new ItemDialogViewModel(catalog, operations, editRow, initialNumbers);
        DataContext = _viewModel;
        _viewModel.FocusRequested += FocusField;
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
    }

    public int? SavedItemId => _viewModel.SavedItemId;

    protected override void OnClosing(CancelEventArgs e)
    {
        if (DialogResult != true
            && _viewModel.HasChanges
            && !DialogHelper.Confirm("Есть несохранённые изменения. Закрыть без сохранения?"))
        {
            e.Cancel = true;
            return;
        }

        base.OnClosing(e);
    }

    private void ItemDialog_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None)
        {
            switch (e.Key)
            {
                case Key.F1:
                    FocusInTab(0, CodeTextBox);
                    e.Handled = true;
                    return;
                case Key.F2:
                    FocusInTab(1, BulkNumbersText);
                    e.Handled = true;
                    return;
                case Key.F3:
                    if (_viewModel.IsNew)
                    {
                        FocusInTab(2, StockQuantityText);
                        e.Handled = true;
                    }

                    return;
            }
        }

        if (Keyboard.Modifiers == ModifierKeys.Control)
        {
            if (e.Key == Key.S)
            {
                _ = TrySaveAsync();
                e.Handled = true;
                return;
            }

            if (e.Key == Key.Enter)
            {
                if (Equals(Keyboard.FocusedElement, BulkNumbersText))
                {
                    AddNumberList_Click(sender, e);
                }
                else
                {
                    _ = TrySaveAsync();
                }

                e.Handled = true;
            }
        }
    }

    private void FocusInTab(int tabIndex, Control control)
    {
        if (tabIndex < FormTabs.Items.Count && FormTabs.SelectedIndex != tabIndex)
        {
            FormTabs.SelectedIndex = tabIndex;
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            control.Focus();
            if (control is TextBox textBox)
            {
                textBox.SelectAll();
            }
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private async void Save_Click(object sender, RoutedEventArgs e)
    {
        await TrySaveAsync();
    }

    private async Task TrySaveAsync()
    {
        if (_viewModel.IsNew
            && !_viewModel.HasPendingOem
            && !_saveWithoutOemConfirmed
            && !DialogHelper.Confirm("Сохранить позицию без OEM? Позицию будет сложнее найти по номеру."))
        {
            FocusInTab(1, BulkNumbersText);
            return;
        }

        _saveWithoutOemConfirmed = true;
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

    private void AddNumber_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddPendingNumber();
    }

    private void RemoveNumber_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.RemovePendingNumber();
    }

    private void AddNumberList_Click(object sender, RoutedEventArgs e)
    {
        _viewModel.AddPendingNumberList();
    }

    private void NewNumberText_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.AddPendingNumber();
            e.Handled = true;
        }
    }

    private void FocusField(string field)
    {
        var (tabIndex, control) = field switch
        {
            "NameTextBox" => (0, (Control)NameTextBox),
            "DiameterText" => (0, DiameterText),
            "ThicknessText" => (0, ThicknessText),
            "WidthText" => (0, WidthText),
            "NewNumberText" => (1, NewNumberText),
            "BulkNumbersText" => (1, BulkNumbersText),
            "StockQuantityText" => (2, StockQuantityText),
            _ => (0, CodeTextBox),
        };

        FocusInTab(tabIndex, control);
    }
}
