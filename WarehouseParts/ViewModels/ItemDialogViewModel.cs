using CommunityToolkit.Mvvm.ComponentModel;
using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.ViewModels;

public partial class ItemDialogViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly CatalogItemRow? _editRow;

    [ObservableProperty]
    private string _code = "";

    [ObservableProperty]
    private string _name = "";

    [ObservableProperty]
    private string _manufacturer = "";

    [ObservableProperty]
    private string _unitText = DisplayTexts.Piece;

    [ObservableProperty]
    private string _applicability = "";

    [ObservableProperty]
    private string _comment = "";

    [ObservableProperty]
    private string _diameterText = "";

    [ObservableProperty]
    private string _thicknessText = "";

    [ObservableProperty]
    private string _widthText = "";

    /// <summary>Id созданной позиции (только при добавлении).</summary>
    public int? SavedItemId { get; private set; }

    public ItemDialogViewModel(CatalogService catalog, CatalogItemRow? editRow)
    {
        _catalog = catalog;
        _editRow = editRow;
        if (editRow is not null)
        {
            _code = editRow.Item.Code;
            _name = editRow.Item.Name;
            _manufacturer = editRow.Item.Manufacturer ?? "";
            _unitText = DisplayTexts.For(editRow.Item.Unit);
            _applicability = editRow.Item.CarApplicability ?? "";
            _comment = editRow.Item.Comment ?? "";
            _diameterText = editRow.Item.Diameter?.ToString() ?? "";
            _thicknessText = editRow.Item.Thickness?.ToString() ?? "";
            _widthText = editRow.Item.Width?.ToString() ?? "";
        }
    }

    public async Task<(bool Ok, string? Error)> SaveAsync()
    {
        try
        {
            var unit = DisplayTexts.ParseUnit(UnitText);
            var diameter = ParseParameter(DiameterText, "Диаметр");
            var thickness = ParseParameter(ThicknessText, "Толщина");
            var width = ParseParameter(WidthText, "Ширина");
            if (_editRow is null)
            {
                var item = await _catalog.AddItemAsync(Code, Name, unit, Manufacturer, Applicability, Comment, diameter, thickness, width);
                SavedItemId = item.Id;
            }
            else
            {
                await _catalog.UpdateItemAsync(
                    _editRow.Item.Id, Code, Name, unit, Manufacturer, Applicability, Comment, diameter, thickness, width);
            }

            return (true, null);
        }
        catch (DomainValidationException ex)
        {
            return (false, ex.Message);
        }
    }

    private static decimal? ParseParameter(string text, string fieldName)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return null;
        }

        if (decimal.TryParse(trimmed, out var value) && value >= 0)
        {
            return value;
        }

        throw new DomainValidationException($"{fieldName} должен быть неотрицательным числом");
    }
}
