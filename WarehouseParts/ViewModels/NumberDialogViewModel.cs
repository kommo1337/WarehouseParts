using CommunityToolkit.Mvvm.ComponentModel;
using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.ViewModels;

public partial class NumberDialogViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly int _itemId;
    private readonly PartNumber? _editNumber;

    [ObservableProperty]
    private string _numberTypeText = DisplayTexts.NumberTypes[0];

    [ObservableProperty]
    private string _numberText = "";

    [ObservableProperty]
    private string _comment = "";

    public NumberDialogViewModel(CatalogService catalog, int itemId, PartNumber? editNumber)
    {
        _catalog = catalog;
        _itemId = itemId;
        _editNumber = editNumber;
        if (editNumber is not null)
        {
            _numberTypeText = DisplayTexts.For(editNumber.NumberType);
            _numberText = editNumber.NumberRaw;
            _comment = editNumber.Comment ?? "";
        }
    }

    public async Task<(bool Ok, string? Error)> SaveAsync()
    {
        try
        {
            var type = DisplayTexts.ParseNumberType(NumberTypeText);
            if (_editNumber is null)
            {
                await _catalog.AddNumberAsync(_itemId, type, NumberText, Comment);
            }
            else
            {
                await _catalog.UpdateNumberAsync(_editNumber.Id, type, NumberText, Comment);
            }

            return (true, null);
        }
        catch (DomainValidationException ex)
        {
            return (false, ex.Message);
        }
    }
}
