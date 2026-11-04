using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.Ui;

namespace WarehouseParts.ViewModels;

/// <summary>
/// Карточка позиции для боковой панели предпросмотра. Обновляется без модальных окон;
/// действия (приход/расход/редактирование) делегируются хост-экрану.
/// </summary>
public partial class ItemDetailsViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly SearchService _search;
    private readonly OperationsService _operations;
    private readonly SemaphoreSlim _loadGate = new(1, 1);
    private CancellationTokenSource? _loadCancellation;
    private int _loadVersion;

    [ObservableProperty]
    private Item? _item;

    [ObservableProperty]
    private int _stock;

    [ObservableProperty]
    private ObservableCollection<NumberRowViewModel> _numbers = new();

    [ObservableProperty]
    private ObservableCollection<RelatedItemRow> _relatedItems = new();

    [ObservableProperty]
    private ObservableCollection<Operation> _recentOperations = new();

    public ItemDetailsViewModel(CatalogService catalog, SearchService search, OperationsService operations)
    {
        _catalog = catalog;
        _search = search;
        _operations = operations;
    }

    public bool HasItem => Item is not null;

    public string Status => Item is null ? "" : StockStatus.FromStock(Stock);

    public string ManufacturerDisplay => string.IsNullOrWhiteSpace(Item?.Manufacturer) ? "—" : Item!.Manufacturer!;

    public string ApplicabilityDisplay => string.IsNullOrWhiteSpace(Item?.CarApplicability) ? "—" : Item!.CarApplicability!;

    public string ParametersDisplay
    {
        get
        {
            if (Item is null)
            {
                return "—";
            }

            var parts = new List<string>();
            if (Item.Diameter is not null)
            {
                parts.Add($"Ø {Item.Diameter} мм");
            }

            if (Item.Thickness is not null)
            {
                parts.Add($"толщина {Item.Thickness} мм");
            }

            if (Item.Width is not null)
            {
                parts.Add($"ширина {Item.Width} мм");
            }

            return parts.Count == 0 ? "—" : string.Join(", ", parts);
        }
    }

    public string CommentDisplay => string.IsNullOrWhiteSpace(Item?.Comment) ? "—" : Item!.Comment!;

    public Func<Item, OperationType, Task>? OpenOperation { get; set; }

    public Func<Item, Task>? OpenEdit { get; set; }

    public Func<Item, Task>? AddNumber { get; set; }

    public async Task LoadAsync(Item item, int stock, CancellationToken cancellationToken = default)
    {
        var version = Interlocked.Increment(ref _loadVersion);
        Item = item;
        Stock = stock;
        Numbers = new ObservableCollection<NumberRowViewModel>();
        RelatedItems = new ObservableCollection<RelatedItemRow>();
        RecentOperations = new ObservableCollection<Operation>();

        _loadCancellation?.Cancel();
        _loadCancellation?.Dispose();
        _loadCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var loadToken = _loadCancellation.Token;

        try
        {
            await _loadGate.WaitAsync(loadToken);
            try
            {
                var numbers = await _catalog.GetNumbersAsync(item.Id, loadToken);
                var related = await _search.GetRelatedItemsAsync(item.Id, loadToken);
                var operations = await _operations.GetRecentOperationsForItemAsync(item.Id, 10, loadToken);
                loadToken.ThrowIfCancellationRequested();
                if (version != _loadVersion)
                {
                    return;
                }

                Numbers = new ObservableCollection<NumberRowViewModel>(numbers.Select(n => new NumberRowViewModel(n)));
                RelatedItems = new ObservableCollection<RelatedItemRow>(related);
                RecentOperations = new ObservableCollection<Operation>(operations);
            }
            finally
            {
                _loadGate.Release();
            }
        }
        catch (OperationCanceledException) when (loadToken.IsCancellationRequested || cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (version == _loadVersion)
            {
                DialogHelper.ShowError(ex);
            }
        }
    }

    public void Clear()
    {
        Interlocked.Increment(ref _loadVersion);
        _loadCancellation?.Cancel();
        Item = null;
        Numbers = new ObservableCollection<NumberRowViewModel>();
        RelatedItems = new ObservableCollection<RelatedItemRow>();
        RecentOperations = new ObservableCollection<Operation>();
    }

    [RelayCommand]
    private async Task ReceiptAsync()
    {
        if (Item is not null && OpenOperation is not null)
        {
            await OpenOperation(Item, OperationType.Receipt);
        }
    }

    [RelayCommand]
    private async Task IssueAsync()
    {
        if (Item is not null && OpenOperation is not null)
        {
            await OpenOperation(Item, OperationType.Issue);
        }
    }

    [RelayCommand]
    private async Task EditAsync()
    {
        if (Item is not null && OpenEdit is not null)
        {
            await OpenEdit(Item);
        }
    }

    [RelayCommand]
    private async Task AddNumberAsync()
    {
        if (Item is not null && AddNumber is not null)
        {
            await AddNumber(Item);
        }
    }

    [RelayCommand]
    private void CopyCode()
    {
        if (Item is null)
        {
            return;
        }

        CopyText(Item.Code, $"Код «{Item.Code}» скопирован");
    }

    [RelayCommand]
    private void CopyNumber(NumberRowViewModel? row)
    {
        if (row is null)
        {
            return;
        }

        CopyText(row.Number.NumberRaw, $"Номер «{row.Number.NumberRaw}» скопирован");
    }

    private static void CopyText(string text, string feedback)
    {
        try
        {
            Clipboard.SetText(text);
            AppFeedback.Send(feedback);
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    partial void OnItemChanged(Item? value)
    {
        OnPropertyChanged(nameof(HasItem));
        OnPropertyChanged(nameof(Status));
        OnPropertyChanged(nameof(ManufacturerDisplay));
        OnPropertyChanged(nameof(ApplicabilityDisplay));
        OnPropertyChanged(nameof(ParametersDisplay));
        OnPropertyChanged(nameof(CommentDisplay));
    }

    partial void OnStockChanged(int value) => OnPropertyChanged(nameof(Status));
}
