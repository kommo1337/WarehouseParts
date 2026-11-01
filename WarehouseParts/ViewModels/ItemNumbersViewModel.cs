using CommunityToolkit.Mvvm.ComponentModel;
using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.ViewModels;

/// <summary>Данные окна «Номера и аналоги позиции». Окно только для просмотра.</summary>
public partial class ItemNumbersViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly SearchService _search;

    [ObservableProperty]
    private IReadOnlyList<NumberRowViewModel> _numbers = Array.Empty<NumberRowViewModel>();

    [ObservableProperty]
    private IReadOnlyList<RelatedItemRow> _relatedItems = Array.Empty<RelatedItemRow>();

    public Item Item { get; }

    public string Title => $"{Item.Code} — {Item.Name}";

    public ItemNumbersViewModel(Item item, CatalogService catalog, SearchService search)
    {
        Item = item;
        _catalog = catalog;
        _search = search;
    }

    public async Task LoadAsync()
    {
        try
        {
            var numbers = await _catalog.GetNumbersAsync(Item.Id);
            Numbers = numbers.Select(n => new NumberRowViewModel(n)).ToList();
            RelatedItems = await _search.GetRelatedItemsAsync(Item.Id);
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }
}
