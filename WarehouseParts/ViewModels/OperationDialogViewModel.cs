using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.ViewModels;

/// <summary>Позиция в списке автоподбора формы операции.</summary>
public class ItemChoice
{
    public ItemChoice(Item item)
    {
        Item = item;
    }

    public Item Item { get; }

    public string DisplayText => Item.Code + " — " + Item.Name;
}

public partial class OperationDialogViewModel : ObservableObject
{
    private readonly OperationsService _operations;
    private readonly CatalogService _catalog;
    private readonly int? _initialItemId;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private CancellationTokenSource? _choicesCancellation;
    private CancellationTokenSource? _stockCancellation;
    private int _stockVersion;

    [ObservableProperty]
    private string _operationTypeText = DisplayTexts.Receipt;

    [ObservableProperty]
    private ObservableCollection<ItemChoice> _itemChoices = new();

    [ObservableProperty]
    private ItemChoice? _selectedChoice;

    [ObservableProperty]
    private string _filterText = "";

    [ObservableProperty]
    private string _quantityText = "";

    [ObservableProperty]
    private string _comment = "";

    [ObservableProperty]
    private string _availableStockText = "";

    public OperationDialogViewModel(
        OperationsService operations,
        CatalogService catalog,
        OperationType? presetType,
        Item? presetItem)
    {
        _operations = operations;
        _catalog = catalog;
        if (presetType.HasValue)
        {
            _operationTypeText = DisplayTexts.For(presetType.Value);
        }

        if (presetItem is not null)
        {
            _filterText = presetItem.Code;
            _selectedChoice = new ItemChoice(presetItem);
            _initialItemId = presetItem.Id;
        }

        _ = ReloadChoicesAsync();
        _ = UpdateAvailableStockAsync();
    }

    /// <summary>Подставляет первое совпадение списка (Enter в поле фильтра).</summary>
    public void PickTopMatch()
    {
        if (SelectedChoice is null && ItemChoices.Count > 0)
        {
            SelectedChoice = ItemChoices[0];
        }
    }

    public void CancelPending()
    {
        _choicesCancellation?.Cancel();
        _stockCancellation?.Cancel();
    }

    /// <summary>Сохраняет операцию. Возвращает false и текст ошибки при неудаче — диалог остается открытым.</summary>
    public async Task<(bool Ok, string? Error)> SaveAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            var type = DisplayTexts.ParseOperation(OperationTypeText);
            var item = await ResolveItemAsync(CancellationToken.None);
            if (item is null)
            {
                throw new DomainValidationException("Укажите запчасть");
            }

            if (!int.TryParse((QuantityText ?? string.Empty).Trim(), out var quantity) || quantity <= 0)
            {
                throw new DomainValidationException("Количество должно быть целым числом больше нуля");
            }

            var comment = string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim();
            await _operations.AddAsync(type, item.Id, quantity, comment);
            return (true, null);
        }
        catch (DomainValidationException ex)
        {
            return (false, ex.Message);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    partial void OnOperationTypeTextChanged(string value) => _ = UpdateAvailableStockAsync();

    partial void OnSelectedChoiceChanged(ItemChoice? value)
    {
        if (value is not null && !string.Equals(FilterText, value.DisplayText, StringComparison.Ordinal))
        {
            FilterText = value.DisplayText;
        }

        _ = UpdateAvailableStockAsync();
    }

    partial void OnFilterTextChanged(string value) => _ = ReloadChoicesAsync();

    private async Task ReloadChoicesAsync()
    {
        _choicesCancellation?.Cancel();
        _choicesCancellation?.Dispose();
        _choicesCancellation = new CancellationTokenSource();
        var cancellationToken = _choicesCancellation.Token;
        var filter = FilterText ?? string.Empty;

        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                var items = await _catalog.SearchItemsAsync(filter, cancellationToken);
                var choices = items.Select(i => new ItemChoice(i)).ToList();
                cancellationToken.ThrowIfCancellationRequested();
                ItemChoices = new ObservableCollection<ItemChoice>(choices);
                SelectedChoice = choices.FirstOrDefault(c => c.DisplayText == filter)
                    ?? choices.FirstOrDefault(c => c.Item.Id == _initialItemId);
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

    private async Task UpdateAvailableStockAsync()
    {
        var version = Interlocked.Increment(ref _stockVersion);
        _stockCancellation?.Cancel();
        _stockCancellation?.Dispose();
        _stockCancellation = new CancellationTokenSource();
        var cancellationToken = _stockCancellation.Token;

        try
        {
            AvailableStockText = string.Empty;
            if (DisplayTexts.ParseOperation(OperationTypeText) != OperationType.Issue)
            {
                return;
            }

            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                var item = await ResolveItemAsync(cancellationToken);
                if (item is null)
                {
                    return;
                }

                var stock = await _operations.GetStockAsync(item.Id, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (version == _stockVersion)
                {
                    AvailableStockText = $"Доступно: {stock}";
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

    private async Task<Item?> ResolveItemAsync(CancellationToken cancellationToken)
    {
        var filter = FilterText ?? string.Empty;
        if (SelectedChoice is not null && string.Equals(SelectedChoice.DisplayText, filter, StringComparison.Ordinal))
        {
            return SelectedChoice.Item;
        }

        var item = await _catalog.FindByCodeAsync(filter, cancellationToken);
        if (item is not null)
        {
            return item;
        }

        var separatorIndex = filter.IndexOf(" — ", StringComparison.Ordinal);
        return separatorIndex > 0
            ? await _catalog.FindByCodeAsync(filter.Substring(0, separatorIndex), cancellationToken)
            : null;
    }
}
