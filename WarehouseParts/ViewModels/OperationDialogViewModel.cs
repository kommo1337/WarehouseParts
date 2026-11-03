using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.ViewModels;

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
    private int? _initialItemId;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private CancellationTokenSource? _choicesCancellation;
    private CancellationTokenSource? _stockCancellation;
    private int _choicesVersion;
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

    [ObservableProperty]
    private bool _isLoadingChoices;

    [ObservableProperty]
    private string _choicesStatusText = "";

    [ObservableProperty]
    private bool _isSaving;

    [ObservableProperty]
    private string _itemError = "";

    [ObservableProperty]
    private string _quantityError = "";

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
            HasPresetItem = true;
        }

        _ = ReloadChoicesAsync();
        _ = UpdateAvailableStockAsync();
    }

    public bool HasPresetItem { get; }

    public bool IsIssue => DisplayTexts.ParseOperation(OperationTypeText) == OperationType.Issue;

    public bool HasSelectedItem => SelectedChoice is not null;

    public bool CanSave => !IsSaving;

    public string TitleText => IsIssue ? "Новый расход" : "Новый приход";

    public string? SavedMessage { get; private set; }

    public event Action<string>? FocusRequested;

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

    public async Task CancelPendingAsync()
    {
        CancelPending();
        await _operationGate.WaitAsync();
        _operationGate.Release();
    }

    public async Task<(bool Ok, string? Error, string? Field)> SaveAsync()
    {
        ItemError = string.Empty;
        QuantityError = string.Empty;
        IsSaving = true;
        await _operationGate.WaitAsync();
        try
        {
            var type = DisplayTexts.ParseOperation(OperationTypeText);
            var item = await ResolveItemAsync(CancellationToken.None);
            if (item is null)
            {
                ItemError = "Укажите запчасть";
                FocusRequested?.Invoke("FilterBox");
                return (false, ItemError, "FilterBox");
            }

            if (!int.TryParse((QuantityText ?? string.Empty).Trim(), out var quantity) || quantity <= 0)
            {
                QuantityError = "Количество должно быть целым числом больше нуля";
                FocusRequested?.Invoke("QuantityBox");
                return (false, QuantityError, "QuantityBox");
            }

            var comment = string.IsNullOrWhiteSpace(Comment) ? null : Comment.Trim();
            await _operations.AddAsync(type, item.Id, quantity, comment);
            SavedMessage = $"{DisplayTexts.For(type)} {quantity} {DisplayTexts.For(item.Unit)} по позиции «{item.Code} — {item.Name}» сохранён";
            return (true, null, null);
        }
        catch (DomainValidationException ex)
        {
            QuantityError = ex.Message;
            FocusRequested?.Invoke("QuantityBox");
            return (false, ex.Message, "QuantityBox");
        }
        finally
        {
            _operationGate.Release();
            IsSaving = false;
        }
    }

    partial void OnOperationTypeTextChanged(string value)
    {
        OnPropertyChanged(nameof(IsIssue));
        OnPropertyChanged(nameof(TitleText));
        _ = UpdateAvailableStockAsync();
    }

    partial void OnSelectedChoiceChanged(ItemChoice? value)
    {
        if (value is not null && !string.Equals(FilterText, value.DisplayText, StringComparison.Ordinal))
        {
            FilterText = value.DisplayText;
        }

        ItemError = string.Empty;
        OnPropertyChanged(nameof(HasSelectedItem));
        _ = UpdateAvailableStockAsync();
    }

    partial void OnFilterTextChanged(string value)
    {
        ItemError = string.Empty;
        if (SelectedChoice is not null && !string.Equals(SelectedChoice.DisplayText, value, StringComparison.Ordinal))
        {
            SelectedChoice = null;
        }
        _ = ReloadChoicesAsync();
    }

    partial void OnQuantityTextChanged(string value) => QuantityError = string.Empty;

    partial void OnIsLoadingChoicesChanged(bool value) => UpdateChoicesStatus();

    partial void OnItemChoicesChanged(ObservableCollection<ItemChoice> value) => UpdateChoicesStatus();

    private async Task ReloadChoicesAsync()
    {
        var version = Interlocked.Increment(ref _choicesVersion);
        _choicesCancellation?.Cancel();
        _choicesCancellation?.Dispose();
        _choicesCancellation = new CancellationTokenSource();
        var cancellationToken = _choicesCancellation.Token;
        var filter = FilterText ?? string.Empty;
        IsLoadingChoices = true;
        UpdateChoicesStatus();

        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                var items = await _catalog.SearchItemsAsync(filter, cancellationToken);
                var choices = items.Select(i => new ItemChoice(i)).ToList();
                cancellationToken.ThrowIfCancellationRequested();
                if (version != _choicesVersion)
                {
                    return;
                }

                ItemChoices = new ObservableCollection<ItemChoice>(choices);
                var initialId = _initialItemId;
                _initialItemId = null;
                SelectedChoice = choices.FirstOrDefault(c => c.DisplayText == filter)
                    ?? choices.FirstOrDefault(c => c.Item.Id == initialId);
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
            if (version == _choicesVersion)
            {
                DialogHelper.ShowError(ex);
            }
        }
        finally
        {
            if (version == _choicesVersion)
            {
                IsLoadingChoices = false;
                UpdateChoicesStatus();
            }
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
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                var item = await ResolveItemAsync(cancellationToken);
                if (item is null || version != _stockVersion)
                {
                    return;
                }

                var stock = await _operations.GetStockAsync(item.Id, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (version == _stockVersion)
                {
                    AvailableStockText = IsIssue
                        ? $"Доступно: {stock} {DisplayTexts.For(item.Unit)}"
                        : $"Остаток: {stock} {DisplayTexts.For(item.Unit)}";
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
            if (version == _stockVersion)
            {
                DialogHelper.ShowError(ex);
            }
        }
    }

    private void UpdateChoicesStatus()
    {
        ChoicesStatusText = IsLoadingChoices
            ? "Поиск позиций…"
            : ItemChoices.Count == 0
                ? "Подходящие позиции не найдены"
                : string.Empty;
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
