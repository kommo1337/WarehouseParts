using System.Collections.ObjectModel;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.ViewModels;

public partial class JournalViewModel : ObservableObject
{
    private const int PageSize = 200;

    public static readonly string[] FilterTypes = { "Все", DisplayTexts.Receipt, DisplayTexts.Issue };

    private readonly OperationsService _operations;
    private readonly DispatcherTimer _filterDebounce;
    private CancellationTokenSource? _loadCancellation;
    private int _pageIndex;
    private int _total;
    private bool _hasLoaded;

    [ObservableProperty]
    private ObservableCollection<JournalRow> _rows = new();

    [ObservableProperty]
    private JournalRow? _selectedRow;

    [ObservableProperty]
    private string _filterCode = "";

    [ObservableProperty]
    private string _filterType = FilterTypes[0];

    [ObservableProperty]
    private DateTime? _dateFrom;

    [ObservableProperty]
    private DateTime? _dateTo;

    [ObservableProperty]
    private string _pageText = "Найдено: 0";

    [ObservableProperty]
    private bool _canPreviousPage;

    [ObservableProperty]
    private bool _canNextPage;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _isEmptyResult;

    public Func<OperationType, Task>? OpenOperationDialog { get; set; }

    public Func<string, bool>? ConfirmDelete { get; set; }

    public bool CanDelete => SelectedRow is not null;

    public bool HasActiveFilters
        => FilterCode.Trim().Length > 0
           || FilterType != FilterTypes[0]
           || DateFrom is not null
           || DateTo is not null;

    public JournalViewModel(OperationsService operations)
    {
        _operations = operations;
        _filterDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _filterDebounce.Tick += async (_, _) =>
        {
            _filterDebounce.Stop();
            await LoadPageAsync(0);
        };
    }

    [RelayCommand]
    public async Task RefreshAsync()
    {
        await LoadPageAsync(_pageIndex);
    }

    public void CancelPending()
    {
        _filterDebounce.Stop();
        _loadCancellation?.Cancel();
    }

    [RelayCommand]
    private async Task AddReceiptAsync()
    {
        await AddAsync(OperationType.Receipt);
    }

    [RelayCommand]
    private async Task AddIssueAsync()
    {
        await AddAsync(OperationType.Issue);
    }

    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        FilterCode = string.Empty;
        FilterType = FilterTypes[0];
        DateFrom = null;
        DateTo = null;
        await LoadPageAsync(0);
    }

    private async Task AddAsync(OperationType type)
    {
        if (OpenOperationDialog is null)
        {
            return;
        }

        await OpenOperationDialog(type);
        await LoadPageAsync(0);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        if (SelectedRow is null)
        {
            return;
        }

        if (ConfirmDelete?.Invoke($"Удалить операцию №{SelectedRow.Id}? Остатки будут пересчитаны.") == false)
        {
            return;
        }

        try
        {
            await _operations.DeleteAsync(SelectedRow.Id);
            await LoadPageAsync(_pageIndex);
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    [RelayCommand]
    private async Task PreviousPageAsync()
    {
        if (CanPreviousPage)
        {
            await LoadPageAsync(_pageIndex - 1);
        }
    }

    [RelayCommand]
    private async Task NextPageAsync()
    {
        if (CanNextPage)
        {
            await LoadPageAsync(_pageIndex + 1);
        }
    }

    partial void OnFilterCodeChanged(string value) { UpdateFilterState(); RestartFilter(); }
    partial void OnFilterTypeChanged(string value) { UpdateFilterState(); RestartFilter(); }
    partial void OnDateFromChanged(DateTime? value) { UpdateFilterState(); RestartFilter(); }
    partial void OnDateToChanged(DateTime? value) { UpdateFilterState(); RestartFilter(); }

    partial void OnSelectedRowChanged(JournalRow? value)
    {
        OnPropertyChanged(nameof(CanDelete));
        DeleteCommand.NotifyCanExecuteChanged();
    }

    private void RestartFilter()
    {
        _filterDebounce.Stop();
        _filterDebounce.Start();
    }

    private async Task LoadPageAsync(int requestedPage)
    {
        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = new CancellationTokenSource();
        var cancellationToken = _loadCancellation.Token;
        IsLoading = true;

        try
        {
            var operationType = FilterType switch
            {
                DisplayTexts.Receipt => OperationType.Receipt,
                DisplayTexts.Issue => OperationType.Issue,
                _ => (OperationType?)null,
            };

            while (true)
            {
                var page = await _operations.GetJournalPageAsync(
                    FilterCode,
                    operationType,
                    DateFrom,
                    DateTo,
                    requestedPage * PageSize,
                    PageSize,
                    cancellationToken);
                var lastPage = page.Total == 0 ? 0 : (page.Total - 1) / PageSize;
                if (requestedPage > lastPage)
                {
                    requestedPage = lastPage;
                    continue;
                }

                cancellationToken.ThrowIfCancellationRequested();
                _pageIndex = requestedPage;
                _total = page.Total;
                Rows = new ObservableCollection<JournalRow>(page.Rows);
                SelectedRow = null;
                _hasLoaded = true;
                IsEmptyResult = _hasLoaded && page.Rows.Count == 0;
                UpdatePageState();
                break;
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
        finally
        {
            IsLoading = false;
        }
    }

    private void UpdateFilterState() => OnPropertyChanged(nameof(HasActiveFilters));

    private void UpdatePageState()
    {
        var pageCount = _total == 0 ? 0 : (_total - 1) / PageSize + 1;
        PageText = _total == 0
            ? "Найдено: 0"
            : $"Страница {_pageIndex + 1} из {pageCount} · Найдено: {_total}";
        CanPreviousPage = _pageIndex > 0;
        CanNextPage = _pageIndex + 1 < pageCount;
    }
}
