using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.Ui;

namespace WarehouseParts.ViewModels;

public class NumberRowViewModel
{
    public NumberRowViewModel(PartNumber number)
    {
        Number = number;
    }

    public PartNumber Number { get; }

    public string TypeDisplay => DisplayTexts.For(Number.NumberType);
}

public readonly record struct ItemDialogResult(bool Saved, int? AddedItemId);

public partial class CatalogViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private CancellationTokenSource? _numbersCancellation;
    private int? _lastAddedItemId;
    private List<CatalogItemRow> _allRows = new();

    [ObservableProperty]
    private ObservableCollection<CatalogItemRow> _rows = new();

    [ObservableProperty]
    private CatalogItemRow? _selectedRow;

    [ObservableProperty]
    private ObservableCollection<NumberRowViewModel> _numbers = new();

    [ObservableProperty]
    private NumberRowViewModel? _selectedNumber;

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private bool _hasActiveFilter;

    [ObservableProperty]
    private string _countText = "Позиций: 0";

    [ObservableProperty]
    private bool _isCatalogEmpty;

    [ObservableProperty]
    private bool _isFilterEmpty;

    [ObservableProperty]
    private string _qualityFilterText = DisplayTexts.CatalogQualityFilters[0];

    [ObservableProperty]
    private string _qualityCountsText = string.Empty;

    public static IReadOnlyList<string> QualityFilterOptions => DisplayTexts.CatalogQualityFilters;

    /// <summary>Карточка боковой панели предпросмотра; null в тестах.</summary>
    public ItemDetailsViewModel? Details { get; }

    public Func<Item, OperationType, Task>? OpenOperation { get; set; }

    public Func<Item, Task>? OpenEditItem { get; set; }

    public Func<Item, Task>? OpenItemAddNumber { get; set; }

    public Func<CatalogItemRow?, Task<ItemDialogResult>>? OpenItemDialog { get; set; }

    public Func<PartNumber?, Task<bool>>? OpenNumberDialog { get; set; }

    public Func<Task>? OpenBulkDialog { get; set; }

    public Func<Item, Task>? OpenItemNumbers { get; set; }

    public Func<string, bool>? ConfirmDelete { get; set; }

    public bool HasSelectedItem => SelectedRow is not null;

    public bool HasSelectedNumber => SelectedNumber is not null;

    public string SelectedItemText => SelectedRow is null
        ? string.Empty
        : $"{SelectedRow.Item.Code} — {SelectedRow.Item.Name}";

    public string NumbersHeader => SelectedRow is null
        ? "Номера позиции"
        : $"Номера позиции «{SelectedItemText}»";

    public string EmptyTitle => IsCatalogEmpty
        ? "Каталог пока пуст"
        : "По фильтру ничего не найдено";

    public string EmptyDescription => IsCatalogEmpty
        ? "Добавьте первую запчасть, затем заполните её номера и остаток."
        : "Измените текст фильтра или добавьте новую позицию.";

    public CatalogViewModel(CatalogService catalog, ItemDetailsViewModel? details = null)
    {
        _catalog = catalog;
        Details = details;
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            var selectedId = SelectedRow?.Item.Id;
            var rows = (await _catalog.GetRowsAsync(ParseQualityFilter(QualityFilterText))).ToList();
            if (_lastAddedItemId.HasValue
                && ParseQualityFilter(QualityFilterText) == CatalogQualityFilter.All)
            {
                var lastAdded = rows.FirstOrDefault(r => r.Item.Id == _lastAddedItemId.Value);
                if (lastAdded is not null)
                {
                    lastAdded.IsLastAdded = true;
                }
            }

            _allRows = rows;
            ApplyFilter(selectedId);

            var counts = await _catalog.GetQualityCountsAsync();
            QualityCountsText = $"Проблемные: без OEM — {counts.WithoutOem}, без производителя — {counts.WithoutManufacturer}, без применимости — {counts.WithoutApplicability}";
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
    private void ClearFilter()
    {
        FilterText = string.Empty;
    }

    [RelayCommand]
    private async Task AddItemAsync()
    {
        if (OpenItemDialog is null)
        {
            return;
        }

        FilterText = string.Empty;
        var result = await OpenItemDialog(null);
        if (!result.Saved)
        {
            return;
        }

        _lastAddedItemId = result.AddedItemId;
        await RefreshAsync();
        if (result.AddedItemId.HasValue)
        {
            SelectedRow = Rows.FirstOrDefault(r => r.Item.Id == result.AddedItemId.Value);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private void CopyItemCode()
    {
        var code = SelectedRow?.Item.Code;
        if (string.IsNullOrEmpty(code))
        {
            return;
        }

        try
        {
            Clipboard.SetText(code);
            AppFeedback.Send($"Код «{code}» скопирован");
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private async Task EditItemAsync()
    {
        if (SelectedRow is null || OpenItemDialog is null)
        {
            return;
        }

        if ((await OpenItemDialog(SelectedRow)).Saved)
        {
            await RefreshAsync();
            ReloadDetails();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private async Task DeleteItemAsync()
    {
        if (SelectedRow is null)
        {
            return;
        }

        var row = SelectedRow;
        if (ConfirmDelete?.Invoke($"Удалить позицию «{row.Item.Code} — {row.Item.Name}»? Все её номера также будут удалены.") == false)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
            try
            {
                await _catalog.DeleteItemAsync(row.Item.Id);
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

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private async Task AddNumberAsync()
    {
        if (SelectedRow is null || OpenNumberDialog is null)
        {
            return;
        }

        if (await OpenNumberDialog(null))
        {
            await LoadNumbersAsync();
        ReloadDetails();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedNumber))]
    private async Task EditNumberAsync()
    {
        if (SelectedNumber is null || OpenNumberDialog is null)
        {
            return;
        }

        if (await OpenNumberDialog(SelectedNumber.Number))
        {
            await LoadNumbersAsync();
        ReloadDetails();
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedNumber))]
    private async Task DeleteNumberAsync()
    {
        if (SelectedNumber is null)
        {
            return;
        }

        var number = SelectedNumber;
        if (ConfirmDelete?.Invoke($"Удалить номер «{number.TypeDisplay}: {number.Number.NumberRaw}»?") == false)
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
            try
            {
                await _catalog.DeleteNumberAsync(number.Number.Id);
            }
            finally
            {
                _operationGate.Release();
            }

            await LoadNumbersAsync();
        ReloadDetails();
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private async Task BulkAddNumbersAsync()
    {
        if (SelectedRow is null || OpenBulkDialog is null)
        {
            return;
        }

        await OpenBulkDialog();
        await LoadNumbersAsync();
        ReloadDetails();
    }

    [RelayCommand(CanExecute = nameof(HasSelectedItem))]
    private async Task ShowNumbersAsync()
    {
        var row = SelectedRow;
        if (row is null || OpenItemNumbers is null)
        {
            return;
        }

        await OpenItemNumbers(row.Item);
    }

    partial void OnFilterTextChanged(string value) => ApplyFilter(SelectedRow?.Item.Id);

    partial void OnSelectedRowChanged(CatalogItemRow? value)
    {
        OnPropertyChanged(nameof(HasSelectedItem));
        OnPropertyChanged(nameof(SelectedItemText));
        OnPropertyChanged(nameof(NumbersHeader));
        CopyItemCodeCommand.NotifyCanExecuteChanged();
        EditItemCommand.NotifyCanExecuteChanged();
        DeleteItemCommand.NotifyCanExecuteChanged();
        AddNumberCommand.NotifyCanExecuteChanged();
        BulkAddNumbersCommand.NotifyCanExecuteChanged();
        ShowNumbersCommand.NotifyCanExecuteChanged();
        if (Details is not null)
        {
            if (value is null)
            {
                Details.Clear();
            }
            else
            {
                _ = Details.LoadAsync(value.Item, value.Stock);
            }
        }

        _ = LoadNumbersAsync();
    }

    partial void OnQualityFilterTextChanged(string value) => _ = RefreshAsync();

    private static CatalogQualityFilter ParseQualityFilter(string text)
    {
        if (text == DisplayTexts.CatalogQualityFilters[1])
        {
            return CatalogQualityFilter.WithoutOem;
        }

        if (text == DisplayTexts.CatalogQualityFilters[2])
        {
            return CatalogQualityFilter.WithoutManufacturer;
        }

        return text == DisplayTexts.CatalogQualityFilters[3]
            ? CatalogQualityFilter.WithoutApplicability
            : CatalogQualityFilter.All;
    }

    private void ReloadDetails()
    {
        var row = SelectedRow;
        if (row is not null && Details is not null)
        {
            _ = Details.LoadAsync(row.Item, row.Stock);
        }
    }

    partial void OnSelectedNumberChanged(NumberRowViewModel? value)
    {
        OnPropertyChanged(nameof(HasSelectedNumber));
        EditNumberCommand.NotifyCanExecuteChanged();
        DeleteNumberCommand.NotifyCanExecuteChanged();
    }

    private void ApplyFilter(int? selectedId)
    {
        var query = (FilterText ?? string.Empty).Trim();
        HasActiveFilter = query.Length > 0;
        var filtered = query.Length == 0
            ? _allRows
            : _allRows.Where(r =>
                Contains(r.Item.Code, query)
                || Contains(r.Item.Name, query)
                || Contains(r.Item.Manufacturer, query)).ToList();

        Rows = new ObservableCollection<CatalogItemRow>(filtered);
        IsCatalogEmpty = _allRows.Count == 0;
        IsFilterEmpty = _allRows.Count > 0 && filtered.Count == 0;
        CountText = HasActiveFilter
            ? $"Показано: {filtered.Count} из {_allRows.Count}"
            : $"Позиций: {_allRows.Count}";
        OnPropertyChanged(nameof(EmptyTitle));
        OnPropertyChanged(nameof(EmptyDescription));

        SelectedRow = selectedId.HasValue
            ? Rows.FirstOrDefault(r => r.Item.Id == selectedId.Value)
            : null;
    }

    private static bool Contains(string? text, string query)
        => text is not null && text.Contains(query, StringComparison.OrdinalIgnoreCase);

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
