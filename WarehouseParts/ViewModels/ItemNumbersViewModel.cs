using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.Ui;

namespace WarehouseParts.ViewModels;

public partial class ItemNumbersViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly SearchService _search;

    [ObservableProperty]
    private IReadOnlyList<NumberRowViewModel> _numbers = Array.Empty<NumberRowViewModel>();

    [ObservableProperty]
    private IReadOnlyList<RelatedItemRow> _relatedItems = Array.Empty<RelatedItemRow>();

    [ObservableProperty]
    private NumberRowViewModel? _selectedNumber;

    [ObservableProperty]
    private RelatedItemRow? _selectedRelatedItem;

    public ItemNumbersViewModel(Item item, CatalogService catalog, SearchService search)
    {
        Item = item;
        _catalog = catalog;
        _search = search;
    }

    public Item Item { get; }

    public string Title => $"{Item.Code} — {Item.Name}";

    public bool HasSelectedNumber => SelectedNumber is not null;

    public bool HasSelectedRelatedItem => SelectedRelatedItem is not null;

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

    [RelayCommand]
    private void CopyNumbers()
    {
        if (Numbers.Count == 0)
        {
            AppFeedback.Send("У выбранной позиции нет номеров");
            return;
        }

        var text = string.Join(
            Environment.NewLine,
            Numbers.Select(n => $"{n.TypeDisplay}\t{n.Number.NumberRaw}\t{n.Number.Comment}"));
        try
        {
            Clipboard.SetText(text);
            AppFeedback.Send($"Скопировано номеров: {Numbers.Count}");
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    [RelayCommand(CanExecute = nameof(HasSelectedRelatedItem))]
    private void CopyRelatedCode()
    {
        var code = SelectedRelatedItem?.Item.Code;
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

    partial void OnSelectedNumberChanged(NumberRowViewModel? value) => OnPropertyChanged(nameof(HasSelectedNumber));

    partial void OnSelectedRelatedItemChanged(RelatedItemRow? value)
    {
        OnPropertyChanged(nameof(HasSelectedRelatedItem));
        CopyRelatedCodeCommand.NotifyCanExecuteChanged();
    }
}
