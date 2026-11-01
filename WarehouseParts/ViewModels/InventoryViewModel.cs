using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarehouseParts.Services;

namespace WarehouseParts.ViewModels;

/// <summary>Оборачивает строку инвентаризации: ввод фактического остатка с проверкой и уведомлениями UI.</summary>
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

    /// <summary>Фактический остаток текстом: пустая строка = не заполнен.</summary>
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
    private readonly InventoryService _inventory;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private CancellationTokenSource? _reloadCancellation;
    private List<InventoryRowViewModel> _allRows = new();

    [ObservableProperty]
    private ObservableCollection<InventoryRowViewModel> _rows = new();

    [ObservableProperty]
    private string _filter = "";

    public InventoryViewModel(InventoryService inventory)
    {
        _inventory = inventory;
    }

    /// <summary>Загружает строки при первом показе экрана.</summary>
    public async Task InitializeAsync()
    {
        if (_allRows.Count > 0)
        {
            ApplyFilter();
            return;
        }

        await ReloadAsync();
    }

    /// <summary>Перечитывает данные при возврате на экран: новые позиции и свежие остатки, введенные факты сохраняются.</summary>
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
                _allRows = rows.Select(r => new InventoryRowViewModel(r, _inventory, () => OnPropertyChanged(nameof(HasUnapplied)))).ToList();
                ApplyFilter();
                OnPropertyChanged(nameof(HasUnapplied));
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

    /// <summary>Есть непримененные ненулевые расхождения.</summary>
    public bool HasUnapplied => _inventory.HasUnappliedDiscrepancies;

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

        Rows = new ObservableCollection<InventoryRowViewModel>(query);
    }
}
