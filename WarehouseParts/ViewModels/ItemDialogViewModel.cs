using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.ViewModels;

public class PendingNumber
{
    public PendingNumber(NumberType numberType, string numberRaw, string? comment)
    {
        NumberType = numberType;
        NumberRaw = numberRaw;
        NumberNormalized = NumberNormalizer.Normalize(numberRaw);
        Comment = comment;
    }

    public NumberType NumberType { get; }

    public string NumberRaw { get; }

    public string NumberNormalized { get; }

    public string? Comment { get; }

    public string TypeDisplay => DisplayTexts.For(NumberType);
}

public partial class ItemDialogViewModel : ObservableObject
{
    private readonly CatalogService _catalog;
    private readonly OperationsService _operations;
    private readonly CatalogItemRow? _editRow;
    private readonly HashSet<(NumberType Type, string Normalized)> _initialNumberKeys = new();

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
    private bool _showExtendedParams;

    [ObservableProperty]
    private string _diameterText = "";

    [ObservableProperty]
    private string _thicknessText = "";

    [ObservableProperty]
    private string _widthText = "";

    [ObservableProperty]
    private string _newNumberTypeText = DisplayTexts.NumberTypes[0];

    [ObservableProperty]
    private string _newNumberText = "";

    [ObservableProperty]
    private string _newNumberComment = "";

    [ObservableProperty]
    private ObservableCollection<PendingNumber> _pendingNumbers = new();

    [ObservableProperty]
    private PendingNumber? _selectedPendingNumber;

    [ObservableProperty]
    private bool _showBulkPanel;

    [ObservableProperty]
    private string _bulkNumberTypeText = DisplayTexts.NumberTypes[0];

    [ObservableProperty]
    private string _bulkNumbersText = "";

    [ObservableProperty]
    private string _bulkPreviewText = "";

    [ObservableProperty]
    private string _bulkFeedbackText = "";

    [ObservableProperty]
    private string _stockQuantityText = "";

    [ObservableProperty]
    private string _stockComment = "";

    [ObservableProperty]
    private string _codeError = "";

    [ObservableProperty]
    private string _nameError = "";

    [ObservableProperty]
    private string _diameterError = "";

    [ObservableProperty]
    private string _thicknessError = "";

    [ObservableProperty]
    private string _widthError = "";

    [ObservableProperty]
    private string _stockQuantityError = "";

    [ObservableProperty]
    private string _newNumberError = "";

    [ObservableProperty]
    private string _bulkNumbersError = "";

    [ObservableProperty]
    private bool _hasChanges;

    public ItemDialogViewModel(
        CatalogService catalog,
        OperationsService operations,
        CatalogItemRow? editRow,
        IReadOnlyList<string>? initialNumbers = null)
    {
        _catalog = catalog;
        _operations = operations;
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
            _showExtendedParams = editRow.Item.Diameter is not null
                || editRow.Item.Thickness is not null
                || editRow.Item.Width is not null;
        }

        if (initialNumbers is not null && IsNew)
        {
            ShowBulkPanel = true;
            BulkNumberTypeText = DisplayTexts.Oem;
            BulkNumbersText = string.Join(Environment.NewLine, initialNumbers);
            HasChanges = false;
        }
    }

    public int? SavedItemId { get; private set; }

    public bool IsNew => _editRow is null;

    public bool IsExisting => _editRow is not null;

    public bool HasPendingOem => PendingNumbers.Any(n => n.NumberType == NumberType.Oem);

    public string WindowTitle => IsNew ? "Новая позиция" : "Изменение позиции";

    public event Action<string>? FocusRequested;

    /// <summary>Загружает существующие номера позиции при редактировании, чтобы к ним можно было добавлять новые.</summary>
    public async Task InitializeAsync()
    {
        if (_editRow is null)
        {
            return;
        }

        try
        {
            var numbers = await _catalog.GetNumbersAsync(_editRow.Item.Id);
            foreach (var number in numbers)
            {
                _initialNumberKeys.Add((number.NumberType, number.NumberNormalized));
                PendingNumbers.Add(new PendingNumber(number.NumberType, number.NumberRaw, number.Comment));
            }

            HasChanges = false;
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    public (bool Ok, string? Error) AddPendingNumber()
    {
        NewNumberError = string.Empty;
        var type = DisplayTexts.ParseNumberType(NewNumberTypeText);
        var raw = (NewNumberText ?? string.Empty).Trim();
        var normalized = NumberNormalizer.Normalize(raw);
        if (normalized.Length == 0)
        {
            NewNumberError = "Введите номер";
            FocusRequested?.Invoke("NewNumberText");
            return (false, NewNumberError);
        }

        if (HasNumber(type, normalized))
        {
            NewNumberError = CatalogService.DuplicateNumberMessage;
            FocusRequested?.Invoke("NewNumberText");
            return (false, NewNumberError);
        }

        PendingNumbers.Add(new PendingNumber(type, raw, TrimToNull(NewNumberComment)));
        NewNumberText = "";
        NewNumberComment = "";
        OnPropertyChanged(nameof(HasPendingOem));
        MarkDirty();
        return (true, null);
    }

    public void RemovePendingNumber()
    {
        if (SelectedPendingNumber is not null)
        {
            PendingNumbers.Remove(SelectedPendingNumber);
            OnPropertyChanged(nameof(HasPendingOem));
            MarkDirty();
        }
    }

    public (int Added, int Skipped, string? Error) AddPendingNumberList()
    {
        BulkNumbersError = string.Empty;
        BulkFeedbackText = string.Empty;
        var type = DisplayTexts.ParseNumberType(BulkNumberTypeText);
        var added = 0;
        var skipped = 0;
        foreach (var raw in NumberListParser.Parse(BulkNumbersText))
        {
            var normalized = NumberNormalizer.Normalize(raw);
            if (HasNumber(type, normalized))
            {
                skipped++;
                continue;
            }

            PendingNumbers.Add(new PendingNumber(type, raw, null));
            added++;
        }

        BulkNumbersText = string.Empty;
        if (added == 0 && skipped == 0)
        {
            BulkNumbersError = "Не найдено ни одного номера";
            FocusRequested?.Invoke("BulkNumbersText");
            return (0, 0, BulkNumbersError);
        }

        OnPropertyChanged(nameof(HasPendingOem));
        MarkDirty();
        BulkFeedbackText = $"Добавлено: {added}, пропущено дубликатов: {skipped}";
        FocusRequested?.Invoke("BulkNumbersText");
        return (added, skipped, null);
    }

    public async Task<(bool Ok, string? Error, string? Field)> SaveAsync()
    {
        CodeError = string.Empty;
        NameError = string.Empty;
        DiameterError = string.Empty;
        ThicknessError = string.Empty;
        WidthError = string.Empty;
        StockQuantityError = string.Empty;

        if (string.IsNullOrWhiteSpace(Code))
        {
            CodeError = "Введите номер запчасти";
            FocusRequested?.Invoke("CodeTextBox");
            return (false, CodeError, "CodeTextBox");
        }

        if (string.IsNullOrWhiteSpace(Name))
        {
            NameError = "Введите наименование";
            FocusRequested?.Invoke("NameTextBox");
            return (false, NameError, "NameTextBox");
        }

        if (!TryParseParameter(DiameterText, "Диаметр", "DiameterText", out var diameter)
            || !TryParseParameter(ThicknessText, "Толщина", "ThicknessText", out var thickness)
            || !TryParseParameter(WidthText, "Ширина", "WidthText", out var width))
        {
            return (false, DiameterError.Length > 0 ? DiameterError : ThicknessError.Length > 0 ? ThicknessError : WidthError,
                DiameterError.Length > 0 ? "DiameterText" : ThicknessError.Length > 0 ? "ThicknessText" : "WidthText");
        }

        int? stockQuantity = null;
        var quantityText = (StockQuantityText ?? string.Empty).Trim();
        if (IsNew && quantityText.Length > 0)
        {
            if (!int.TryParse(quantityText, out var quantity) || quantity <= 0)
            {
                StockQuantityError = "Количество товара должно быть целым числом больше нуля";
                FocusRequested?.Invoke("StockQuantityText");
                return (false, StockQuantityError, "StockQuantityText");
            }

            stockQuantity = quantity;
        }

        try
        {
            var unit = DisplayTexts.ParseUnit(UnitText);
            if (IsNew)
            {
                var item = await _catalog.AddItemAsync(Code, Name, unit, Manufacturer, Applicability, Comment, diameter, thickness, width);
                SavedItemId = item.Id;
                foreach (var number in PendingNumbers)
                {
                    await _catalog.AddNumberAsync(item.Id, number.NumberType, number.NumberRaw, number.Comment);
                }

                if (stockQuantity.HasValue)
                {
                    await _operations.AddAsync(OperationType.Receipt, item.Id, stockQuantity.Value, TrimToNull(StockComment));
                }
            }
            else
            {
                await _catalog.UpdateItemAsync(
                    _editRow!.Item.Id, Code, Name, unit, Manufacturer, Applicability, Comment, diameter, thickness, width);
                foreach (var number in PendingNumbers)
                {
                    if (_initialNumberKeys.Contains((number.NumberType, number.NumberNormalized)))
                    {
                        continue;
                    }

                    await _catalog.AddNumberAsync(_editRow.Item.Id, number.NumberType, number.NumberRaw, number.Comment);
                    _initialNumberKeys.Add((number.NumberType, number.NumberNormalized));
                }
            }

            HasChanges = false;
            return (true, null, null);
        }
        catch (DomainValidationException ex)
        {
            if (ex.Message == CatalogService.DuplicateCodeMessage)
            {
                CodeError = ex.Message;
                FocusRequested?.Invoke("CodeTextBox");
                return (false, ex.Message, "CodeTextBox");
            }

            StockQuantityError = ex.Message;
            FocusRequested?.Invoke("StockQuantityText");
            return (false, ex.Message, "StockQuantityText");
        }
    }

    private bool TryParseParameter(string text, string fieldName, string field, out decimal? value)
    {
        value = null;
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (decimal.TryParse(trimmed, out var parsed) && parsed >= 0)
        {
            value = parsed;
            return true;
        }

        var error = $"{fieldName} должен быть неотрицательным числом";
        if (field == "DiameterText")
        {
            DiameterError = error;
        }
        else if (field == "ThicknessText")
        {
            ThicknessError = error;
        }
        else
        {
            WidthError = error;
        }
        FocusRequested?.Invoke(field);
        return false;
    }

    private bool HasNumber(NumberType type, string normalized)
        => PendingNumbers.Any(n => n.NumberType == type && n.NumberNormalized == normalized);

    private void UpdateBulkPreview()
    {
        var parsed = NumberListParser.Parse(BulkNumbersText);
        if (parsed.Count == 0)
        {
            BulkPreviewText = string.Empty;
            return;
        }

        var type = DisplayTexts.ParseNumberType(BulkNumberTypeText);
        var unique = parsed.Select(NumberNormalizer.Normalize).ToHashSet(StringComparer.Ordinal);
        var duplicates = parsed.Count - unique.Count;
        var alreadyAdded = unique.Count(n => HasNumber(type, n));
        var parts = new List<string> { $"номеров: {parsed.Count}" };
        if (duplicates > 0)
        {
            parts.Add($"дубликатов в списке: {duplicates}");
        }

        if (alreadyAdded > 0)
        {
            parts.Add($"уже добавлено: {alreadyAdded}");
        }

        BulkPreviewText = string.Join(", ", parts);
    }

    private void MarkDirty() => HasChanges = true;

    private static string? TrimToNull(string? value)
    {
        var trimmed = value?.Trim();
        return string.IsNullOrEmpty(trimmed) ? null : trimmed;
    }

    partial void OnCodeChanged(string value) { CodeError = string.Empty; MarkDirty(); }
    partial void OnNameChanged(string value) { NameError = string.Empty; MarkDirty(); }
    partial void OnManufacturerChanged(string value) => MarkDirty();
    partial void OnUnitTextChanged(string value) => MarkDirty();
    partial void OnApplicabilityChanged(string value) => MarkDirty();
    partial void OnCommentChanged(string value) => MarkDirty();
    partial void OnShowExtendedParamsChanged(bool value) => MarkDirty();
    partial void OnDiameterTextChanged(string value) { DiameterError = string.Empty; MarkDirty(); }
    partial void OnThicknessTextChanged(string value) { ThicknessError = string.Empty; MarkDirty(); }
    partial void OnWidthTextChanged(string value) { WidthError = string.Empty; MarkDirty(); }
    partial void OnNewNumberTypeTextChanged(string value) => MarkDirty();
    partial void OnNewNumberTextChanged(string value) { NewNumberError = string.Empty; MarkDirty(); }
    partial void OnNewNumberCommentChanged(string value) => MarkDirty();
    partial void OnShowBulkPanelChanged(bool value) => MarkDirty();
    partial void OnBulkNumberTypeTextChanged(string value) { UpdateBulkPreview(); MarkDirty(); }
    partial void OnBulkNumbersTextChanged(string value)
    {
        BulkNumbersError = string.Empty;
        BulkFeedbackText = string.Empty;
        UpdateBulkPreview();
        MarkDirty();
    }
    partial void OnStockQuantityTextChanged(string value) { StockQuantityError = string.Empty; MarkDirty(); }
    partial void OnStockCommentChanged(string value) => MarkDirty();
}
