using System.Collections.ObjectModel;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarehouseParts.Data;
using WarehouseParts.Services;

namespace WarehouseParts.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly SearchService _search;
    private readonly DispatcherTimer _debounce;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private CancellationTokenSource? _searchCancellation;
    private int _searchVersion;

    [ObservableProperty]
    private string _quickQuery = "";

    [ObservableProperty]
    private bool _isQuickMode = true;

    [ObservableProperty]
    private string _advName = "";

    [ObservableProperty]
    private string _advCode = "";

    [ObservableProperty]
    private string _advNumber = "";

    [ObservableProperty]
    private string _advNumberType = DisplayTexts.NumberTypeFilters[0];

    [ObservableProperty]
    private string _advManufacturer = "";

    [ObservableProperty]
    private string _advApplicability = "";

    [ObservableProperty]
    private string _advAvailability = DisplayTexts.AvailabilityFilters[0];

    [ObservableProperty]
    private string _advDiameter = "";

    [ObservableProperty]
    private string _advThickness = "";

    [ObservableProperty]
    private string _advWidth = "";

    [ObservableProperty]
    private string _advTolerance = "0";

    [ObservableProperty]
    private ObservableCollection<SearchResultRow> _rows = new();

    [ObservableProperty]
    private string _counterText = "Найдено: 0 (0 прямых, 0 аналогов)";

    [ObservableProperty]
    private string _catalogInfoText = "";

    public static IReadOnlyList<string> NumberTypeFilterOptions => DisplayTexts.NumberTypeFilters;

    public static IReadOnlyList<string> AvailabilityFilterOptions => DisplayTexts.AvailabilityFilters;

    /// <summary>Режим «Расширенный поиск» — инверсия IsQuickMode для второго переключателя.</summary>
    public bool IsAdvancedMode
    {
        get => !IsQuickMode;
        set => IsQuickMode = !value;
    }

    /// <summary>Открывает форму операции с предзаполненной запчастью и типом; возвращает управление после сохранения.</summary>
    public Func<Item, OperationType, Task>? OpenOperation { get; set; }

    /// <summary>Открывает окно «Номера и аналоги позиции».</summary>
    public Func<Item, Task>? OpenItemNumbers { get; set; }

    public SearchViewModel(SearchService search)
    {
        _search = search;
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await ExecuteSearchAsync();
        };
    }

    public async Task RefreshIndicatorsAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            var counts = await _search.GetCatalogCountsAsync();
            CatalogInfoText = $"Всего позиций: {counts.Total}   В наличии: {counts.InStock}   Нет в наличии: {counts.OutOfStock}";
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
        finally
        {
            _operationGate.Release();
        }
    }

    /// <summary>Обновляет данные экрана при возврате в раздел: результаты и показатели по актуальным данным.</summary>
    public async Task RefreshOnShowAsync()
    {
        await ExecuteSearchAsync();
        await RefreshIndicatorsAsync();
    }

    public void CancelPending()
    {
        _debounce.Stop();
        Interlocked.Increment(ref _searchVersion);
        _searchCancellation?.Cancel();
    }

    [RelayCommand]
    private async Task FindAsync()
    {
        await ExecuteSearchAsync();
    }

    [RelayCommand]
    private async Task OperateReceiptAsync(SearchResultRow? row)
    {
        await OperateAsync(row, OperationType.Receipt);
    }

    [RelayCommand]
    private async Task OperateIssueAsync(SearchResultRow? row)
    {
        await OperateAsync(row, OperationType.Issue);
    }

    [RelayCommand]
    private async Task OpenNumbersAsync(SearchResultRow? row)
    {
        if (row is null || OpenItemNumbers is null)
        {
            return;
        }

        await OpenItemNumbers(row.Item);
    }

    [RelayCommand]
    private void Copy()
    {
        var builder = new StringBuilder();
        builder.AppendLine("Номер запчасти\tНаименование\tПроизводитель\tПрименимость к автомобилю\tЕдиница измерения\tТекущий остаток\tСтатус наличия\tТип результата\tНайденный или общий номер");
        foreach (var row in Rows)
        {
            builder.AppendLine(
                $"{row.Item.Code}\t{row.Item.Name}\t{row.Item.Manufacturer}\t{row.Item.CarApplicability}\t" +
                $"{DisplayTexts.For(row.Item.Unit)}\t{row.Stock}\t{row.Status}\t{row.ResultTypeDisplay}\t{row.MatchedNumber}");
        }

        try
        {
            Clipboard.SetText(builder.ToString());
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    private async Task OperateAsync(SearchResultRow? row, OperationType type)
    {
        if (row is null || OpenOperation is null)
        {
            return;
        }

        await OpenOperation(row.Item, type);
        await ExecuteSearchAsync();
        await RefreshIndicatorsAsync();
    }

    private async Task ExecuteSearchAsync()
    {
        var version = Interlocked.Increment(ref _searchVersion);
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
        _searchCancellation = new CancellationTokenSource();
        var cancellationToken = _searchCancellation.Token;
        var isQuickMode = IsQuickMode;
        var quickQuery = QuickQuery;
        SearchCriteria? criteria = null;

        try
        {
            if (!isQuickMode)
            {
                if (!TryParseParameter(AdvDiameter, "Диаметр", out var diameter)
                    || !TryParseParameter(AdvThickness, "Толщина", out var thickness)
                    || !TryParseParameter(AdvWidth, "Ширина", out var width)
                    || !TryParseTolerance(out var tolerance))
                {
                    return;
                }

                criteria = new SearchCriteria
                {
                    Name = AdvName,
                    Code = AdvCode,
                    Number = AdvNumber,
                    NumberFilter = ParseNumberFilter(AdvNumberType),
                    Manufacturer = AdvManufacturer,
                    CarApplicability = AdvApplicability,
                    Availability = ParseAvailability(AdvAvailability),
                    Diameter = diameter,
                    Thickness = thickness,
                    Width = width,
                    TolerancePercent = tolerance
                };
            }

            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                var result = isQuickMode
                    ? await _search.SearchQuickAsync(quickQuery, cancellationToken)
                    : await _search.SearchAdvancedAsync(criteria!, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (version == _searchVersion)
                {
                    ApplyResult(result);
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

    private void ApplyResult(SearchResult result)
    {
        Rows = new ObservableCollection<SearchResultRow>(result.Rows);
        CounterText = $"Найдено: {result.Total} ({result.DirectCount} прямых, {result.AnalogCount} аналогов)";
    }

    private void RestartDebounce()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private static NumberTypeFilter ParseNumberFilter(string text) => text switch
    {
        DisplayTexts.Oem => NumberTypeFilter.Oem,
        DisplayTexts.Article => NumberTypeFilter.Article,
        DisplayTexts.Analog => NumberTypeFilter.Analog,
        _ => NumberTypeFilter.Any,
    };

    private static AvailabilityFilter ParseAvailability(string text) => text switch
    {
        StockStatus.InStock => AvailabilityFilter.InStock,
        StockStatus.OutOfStock => AvailabilityFilter.OutOfStock,
        _ => AvailabilityFilter.All,
    };

    private bool TryParseParameter(string text, string fieldName, out decimal? value)
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

        DialogHelper.ShowError(new DomainValidationException($"{fieldName} должен быть неотрицательным числом"));
        return false;
    }

    private bool TryParseTolerance(out decimal tolerance)
    {
        tolerance = 0m;
        var trimmed = (AdvTolerance ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return true;
        }

        if (decimal.TryParse(trimmed, out var parsed) && parsed >= 0)
        {
            tolerance = parsed;
            return true;
        }

        DialogHelper.ShowError(new DomainValidationException("Разброс должен быть неотрицательным числом (проценты)"));
        return false;
    }

    partial void OnQuickQueryChanged(string value) => RestartDebounce();
    partial void OnIsQuickModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAdvancedMode));
        RestartDebounce();
    }
    partial void OnAdvNameChanged(string value) => RestartDebounce();
    partial void OnAdvCodeChanged(string value) => RestartDebounce();
    partial void OnAdvNumberChanged(string value) => RestartDebounce();
    partial void OnAdvNumberTypeChanged(string value) => RestartDebounce();
    partial void OnAdvManufacturerChanged(string value) => RestartDebounce();
    partial void OnAdvApplicabilityChanged(string value) => RestartDebounce();
    partial void OnAdvAvailabilityChanged(string value) => RestartDebounce();
    partial void OnAdvDiameterChanged(string value) => RestartDebounce();
    partial void OnAdvThicknessChanged(string value) => RestartDebounce();
    partial void OnAdvWidthChanged(string value) => RestartDebounce();
    partial void OnAdvToleranceChanged(string value) => RestartDebounce();
}
