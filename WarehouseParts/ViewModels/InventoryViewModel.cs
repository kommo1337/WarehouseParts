using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarehouseParts.Services;
using WarehouseParts.Ui;

namespace WarehouseParts.ViewModels;

public class InventoryRowViewModel : ObservableObject
{
    private readonly InventoryRow _row;
    private readonly InventoryService _service;
    private readonly Action _onChanged;

    public InventoryRowViewModel(InventoryRow row, InventoryService service, Action onChanged)
    {
        _row = row;
        _service = service;
        _onChanged = onChanged;
    }

    public int ItemId => _row.ItemId;

    public string Code => _row.Code;

    public string Name => _row.Name;

    public int Stock => _row.Stock;

    public int? Discrepancy => _row.Discrepancy;

    public string ActionLabel => _row.ActionLabel;

    public bool CanApply => _row.Action is InventoryAction.Receipt or InventoryAction.Issue;

    public string FactInput
    {
        get => _row.Fact?.ToString() ?? "";
        set => SetFact(value);
    }

    public async Task ApplyAsync(DateTime now)
    {
        await _service.ApplyAsync(ItemId, now);
        RaiseRowChanged();
    }

    private void SetFact(string? value)
    {
        var text = (value ?? "").Trim();
        try
        {
            if (text.Length == 0)
            {
                _service.SetFact(ItemId, null);
            }
            else if (int.TryParse(text, out var fact) && fact >= 0)
            {
                _service.SetFact(ItemId, fact);
            }
            else
            {
                throw new DomainValidationException("Фактический остаток должен быть целым числом не меньше нуля");
            }
        }
        catch (DomainValidationException ex)
        {
            DialogHelper.ShowError(ex);
            OnPropertyChanged(nameof(FactInput));
            return;
        }

        RaiseRowChanged();
    }

    private void RaiseRowChanged()
    {
        OnPropertyChanged(nameof(FactInput));
        OnPropertyChanged(nameof(Stock));
        OnPropertyChanged(nameof(Discrepancy));
        OnPropertyChanged(nameof(ActionLabel));
        OnPropertyChanged(nameof(CanApply));
        _onChanged();
    }
}

public partial class InventoryViewModel : ObservableObject
{
    public static readonly string[] DiscrepancyFilterOptions = { "Все", "Есть расхождения", "Без расхождений", "Факт не введён" };

    private readonly InventoryService _inventory;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private CancellationTokenSource? _reloadCancellation;
    private List<InventoryRowViewModel> _allRows = new();

    [ObservableProperty]
    private ObservableCollection<InventoryRowViewModel> _rows = new();

    [ObservableProperty]
    private string _filter = "";

    [ObservableProperty]
    private string _discrepancyFilter = DiscrepancyFilterOptions[0];

    [ObservableProperty]
    private string _countText = "Позиций: 0";

    [ObservableProperty]
    private bool _isEmptyResult;

    public InventoryViewModel(InventoryService inventory)
    {
        _inventory = inventory;
    }

    public bool HasUnapplied => _allRows.Any(r => r.Discrepancy.GetValueOrDefault() != 0);

    public string UnappliedText => HasUnapplied
        ? $"Неприменённые расхождения: {_allRows.Count(r => r.Discrepancy.GetValueOrDefault() != 0)}"
        : "Неприменённых расхождений нет";

    public bool HasActiveFilters => Filter.Trim().Length > 0 || DiscrepancyFilter != DiscrepancyFilterOptions[0];

    public async Task InitializeAsync()
    {
        if (_allRows.Count > 0)
        {
            ApplyFilter();
            return;
        }

        await ReloadAsync();
    }

    public async Task ReloadAsync()
    {
        _reloadCancellation?.Cancel();
        _reloadCancellation?.Dispose();
        _reloadCancellation = new CancellationTokenSource();
        var cancellationToken = _reloadCancellation.Token;

        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                var rows = await _inventory.ReloadRowsAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                _allRows = rows.Select(r => new InventoryRowViewModel(r, _inventory, RowChanged)).ToList();
                ApplyFilter();
                RowChanged();
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

    public void CancelPending()
    {
        _reloadCancellation?.Cancel();
    }

    /// <summary>
    /// Сбрасывает состояние экрана после подтверждённого ухода: отменяет загрузку
    /// и очищает введённые фактические остатки, чтобы следующий вход начинался заново.
    /// </summary>
    public void Reset()
    {
        CancelPending();
        _inventory.ClearSession();
        _allRows = new List<InventoryRowViewModel>();
        Rows = new ObservableCollection<InventoryRowViewModel>();
        CountText = "Позиций: 0";
        IsEmptyResult = false;
        RowChanged();
    }

    [RelayCommand]
    private void ClearFilters()
    {
        Filter = string.Empty;
        DiscrepancyFilter = DiscrepancyFilterOptions[0];
    }

    [RelayCommand]
    private async Task ApplyRowAsync(InventoryRowViewModel? row)
    {
        if (row is null || !row.CanApply)
        {
            return;
        }

        if (!DialogHelper.Confirm($"Провести операцию «{row.ActionLabel}» по позиции «{row.Code} — {row.Name}»?"))
        {
            return;
        }

        try
        {
            await _operationGate.WaitAsync();
            try
            {
                await row.ApplyAsync(DateTime.Now);
                ApplyFilter();
                RowChanged();
                AppFeedback.Send($"Корректировка «{row.ActionLabel}» по позиции «{row.Code} — {row.Name}» проведена");
            }
            finally
            {
                _operationGate.Release();
            }
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    partial void OnDiscrepancyFilterChanged(string value) => ApplyFilter();

    private void RowChanged()
    {
        OnPropertyChanged(nameof(HasUnapplied));
        OnPropertyChanged(nameof(UnappliedText));
    }

    private void ApplyFilter()
    {
        var text = (Filter ?? "").Trim();
        IEnumerable<InventoryRowViewModel> query = _allRows;
        if (text.Length > 0)
        {
            query = query.Where(r =>
                r.Code.Contains(text, StringComparison.OrdinalIgnoreCase)
                || r.Name.Contains(text, StringComparison.OrdinalIgnoreCase));
        }

        query = DiscrepancyFilter switch
        {
            "Есть расхождения" => query.Where(r => r.Discrepancy.GetValueOrDefault() != 0),
            "Без расхождений" => query.Where(r => r.Discrepancy.HasValue && r.Discrepancy.Value == 0),
            "Факт не введён" => query.Where(r => !r.Discrepancy.HasValue),
            _ => query,
        };

        var rows = query.ToList();
        Rows = new ObservableCollection<InventoryRowViewModel>(rows);
        CountText = HasActiveFilters
            ? $"Показано: {rows.Count} из {_allRows.Count}"
            : $"Позиций: {_allRows.Count}";
        IsEmptyResult = _allRows.Count > 0 && rows.Count == 0;
        OnPropertyChanged(nameof(HasActiveFilters));
    }
}
