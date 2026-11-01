using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.ViewModels;

/// <summary>Номер позиции с отображаемым типом.</summary>
public class NumberRowViewModel
{
    public NumberRowViewModel(PartNumber number)
    {
        Number = number;
    }

    public PartNumber Number { get; }

    public string TypeDisplay => DisplayTexts.For(Number.NumberType);
}

public partial class CatalogViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private CancellationTokenSource? _numbersCancellation;

    [ObservableProperty]
    private ObservableCollection<CatalogItemRow> _rows = new();

    [ObservableProperty]
    private CatalogItemRow? _selectedRow;

    [ObservableProperty]
    private ObservableCollection<NumberRowViewModel> _numbers = new();

    [ObservableProperty]
    private NumberRowViewModel? _selectedNumber;

    /// <summary>Диалог добавления (null) или редактирования позиции. true — данные сохранены.</summary>
    public Func<CatalogItemRow?, Task<bool>>? OpenItemDialog { get; set; }

    /// <summary>Диалог добавления (null) или редактирования выбранного номера. true — данные сохранены.</summary>
    public Func<PartNumber?, Task<bool>>? OpenNumberDialog { get; set; }

    /// <summary>Диалог массового ввода номеров.</summary>
    public Func<Task>? OpenBulkDialog { get; set; }

    /// <summary>Окно «Номера и аналоги позиции».</summary>
    public Func<Item, Task>? OpenItemNumbers { get; set; }

    public CatalogViewModel(CatalogService catalog)
    {
        _catalog = catalog;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            var selectedId = SelectedRow?.Item.Id;
            Rows = new ObservableCollection<CatalogItemRow>(await _catalog.GetRowsAsync());
            Numbers = new ObservableCollection<NumberRowViewModel>();
            SelectedNumber = null;
            SelectedRow = selectedId.HasValue
                ? Rows.FirstOrDefault(r => r.Item.Id == selectedId.Value)
                : null;
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    [RelayCommand]
    private async Task AddItemAsync()
    {
        if (OpenItemDialog is null)
        {
            return;
        }

        if (await OpenItemDialog(null))
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task EditItemAsync()
    {
        if (SelectedRow is null || OpenItemDialog is null)
        {
            return;
        }

        if (await OpenItemDialog(SelectedRow))
        {
            await RefreshAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteItemAsync()
    {
        if (SelectedRow is null)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
            try
            {
                await _catalog.DeleteItemAsync(SelectedRow.Item.Id);
            }
            finally
            {
                _operationGate.Release();
            }

            await RefreshAsync();
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task AddNumberAsync()
    {
        if (SelectedRow is null || OpenNumberDialog is null)
        {
            return;
        }

        if (await OpenNumberDialog(null))
        {
            await LoadNumbersAsync();
        }
    }

    [RelayCommand]
    private async Task EditNumberAsync()
    {
        if (SelectedNumber is null || OpenNumberDialog is null)
        {
            return;
        }

        if (await OpenNumberDialog(SelectedNumber.Number))
        {
            await LoadNumbersAsync();
        }
    }

    [RelayCommand]
    private async Task DeleteNumberAsync()
    {
        if (SelectedNumber is null)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
            try
            {
                await _catalog.DeleteNumberAsync(SelectedNumber.Number.Id);
            }
            finally
            {
                _operationGate.Release();
            }

            await LoadNumbersAsync();
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task BulkAddNumbersAsync()
    {
        if (SelectedRow is null || OpenBulkDialog is null)
        {
            return;
        }

        await OpenBulkDialog();
        await LoadNumbersAsync();
    }

    [RelayCommand]
    private async Task ShowNumbersAsync()
    {
        var row = SelectedRow;
        if (row is null || OpenItemNumbers is null)
        {
            return;
        }

        await OpenItemNumbers(row.Item);
    }

    partial void OnSelectedRowChanged(CatalogItemRow? value)
    {
        _ = LoadNumbersAsync();
    }

    private async Task LoadNumbersAsync()
    {
        _numbersCancellation?.Cancel();
        _numbersCancellation?.Dispose();
        _numbersCancellation = new CancellationTokenSource();
        var cancellationToken = _numbersCancellation.Token;
        var itemId = SelectedRow?.Item.Id;

        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                var numbers = itemId is null
                    ? new List<PartNumber>()
                    : await _catalog.GetNumbersAsync(itemId.Value, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (itemId == SelectedRow?.Item.Id)
                {
                    Numbers = new ObservableCollection<NumberRowViewModel>(numbers.Select(n => new NumberRowViewModel(n)));
                    SelectedNumber = null;
                }
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }
}
