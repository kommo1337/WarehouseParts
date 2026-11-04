using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.Ui;

namespace WarehouseParts.ViewModels;

public partial class SearchViewModel : ObservableObject
{
    private readonly SearchService _search;
    private readonly UiSettingsService? _uiSettings;
    private readonly DispatcherTimer _debounce;
    private readonly SemaphoreSlim _operationGate = new(1, 1);
    private CancellationTokenSource? _searchCancellation;
    private CancellationTokenSource? _massSearchCancellation;
    private int _searchVersion;

    /// <summary>Максимальное количество номеров в одном массовом поиске.</summary>
    public const int MaxBulkNumbers = 500;

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
    private ListCollectionView _rowsView = new(new ObservableCollection<SearchResultRow>());

    [ObservableProperty]
    private SearchResultRow? _selectedRow;

    [ObservableProperty]
    private string _confirmationText = "";

    [ObservableProperty]
    private string _bulkInputText = "";

    [ObservableProperty]
    private bool _showBulkChoice;

    [ObservableProperty]
    private bool _showMassResult;

    [ObservableProperty]
    private ObservableCollection<string> _massSummaryLines = new();

    [ObservableProperty]
    private string _massNotFoundText = "";

    [ObservableProperty]
    private bool _previewVisible = true;

    public bool IsPreviewHidden => !PreviewVisible;

    [ObservableProperty]
    private string _counterText = "Найдено: 0 (0 прямых, 0 аналогов)";

    [ObservableProperty]
    private string _catalogInfoText = "";

    [ObservableProperty]
    private bool _isSearching;

    [ObservableProperty]
    private bool _hasSearched;

    [ObservableProperty]
    private string _searchStatusText = "";

    [ObservableProperty]
    private string _searchErrorText = "";

    [ObservableProperty]
    private bool _hasActiveFilters;

    [ObservableProperty]
    private string _activeFilterSummary = "";

    [ObservableProperty]
    private int _catalogTotal;

    public bool IsSearchError => SearchErrorText.Length > 0;

    public bool IsEmptyResult => HasSearched && Rows.Count == 0 && !IsSearching && !ShowMassResult;

    public bool HasSelectedRow => SelectedRow is not null;

    public string EmptyStateTitle => CatalogTotal == 0
        ? "Каталог пока пуст"
        : "Ничего не найдено";

    public string EmptyStateDescription => CatalogTotal == 0
        ? "Добавьте первую запчасть, затем заполните её номера и остаток."
        : HasActiveFilters
            ? "Измените условия поиска или сбросьте фильтры."
            : "Попробуйте ввести другой код, наименование, производителя, применимость или номер.";

    public static IReadOnlyList<string> NumberTypeFilterOptions => DisplayTexts.NumberTypeFilters;

    public static IReadOnlyList<string> AvailabilityFilterOptions => DisplayTexts.AvailabilityFilters;

    public bool IsAdvancedMode
    {
        get => !IsQuickMode;
        set => IsQuickMode = !value;
    }

    public bool CanClearQuick => QuickQuery.Length > 0;

    public Func<Item, OperationType, Task>? OpenOperation { get; set; }

    public Func<Item, Task>? OpenItemNumbers { get; set; }

    public Func<Task>? OpenCatalog { get; set; }

    /// <summary>Открывает форму новой позиции со списком OEM, вставленным в массовое поле.</summary>
    public Func<IReadOnlyList<string>, Task>? OpenAddItemWithNumbers { get; set; }

    /// <summary>Открывает форму редактирования позиции.</summary>
    public Func<Item, Task>? OpenEditItem { get; set; }

    /// <summary>
    /// Вызывается после входа в массовый поиск, до его выполнения; точка наблюдения
    /// для тестов конкурентности (например, чтобы отменить поиск в этом моменте).
    /// </summary>
    public Func<Task>? MassSearchStarted { get; set; }

    /// <summary>Карточка боковой панели предпросмотра; null, если сервисы номеров недоступны (тесты).</summary>
    public ItemDetailsViewModel? Details { get; }

    public SearchViewModel(SearchService search, ItemDetailsViewModel? details = null, UiSettingsService? uiSettings = null)
    {
        _search = search;
        Details = details;
        _uiSettings = uiSettings;
        _previewVisible = uiSettings?.PreviewVisible ?? true;
        RowsView = CreateRowsView(Rows);
        _debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _debounce.Tick += async (_, _) =>
        {
            _debounce.Stop();
            await ExecuteSearchAsync();
        };
        UpdateFilterState();
    }

    public async Task RefreshIndicatorsAsync()
    {
        await _operationGate.WaitAsync();
        try
        {
            var counts = await _search.GetCatalogCountsAsync();
            CatalogTotal = counts.Total;
            UpdateResultState();
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
        _massSearchCancellation?.Cancel();
    }

    [RelayCommand]
    private async Task FindAsync()
    {
        await ExecuteSearchAsync();
    }

    [RelayCommand]
    private void ClearQuick()
    {
        QuickQuery = string.Empty;
    }

    [RelayCommand]
    private async Task ResetFiltersAsync()
    {
        QuickQuery = string.Empty;
        AdvName = string.Empty;
        AdvCode = string.Empty;
        AdvNumber = string.Empty;
        AdvNumberType = DisplayTexts.NumberTypeFilters[0];
        AdvManufacturer = string.Empty;
        AdvApplicability = string.Empty;
        AdvAvailability = DisplayTexts.AvailabilityFilters[0];
        AdvDiameter = string.Empty;
        AdvThickness = string.Empty;
        AdvWidth = string.Empty;
        AdvTolerance = "0";
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
    private async Task OpenNumbersAsync()
    {
        var row = SelectedRow;
        if (row is null || OpenItemNumbers is null)
        {
            return;
        }

        await OpenItemNumbers(row.Item);
    }

    [RelayCommand]
    private async Task GoToCatalogAsync()
    {
        if (OpenCatalog is not null)
        {
            await OpenCatalog();
        }
    }

    [RelayCommand]
    private async Task SearchAllNumbersAsync()
    {
        var numbers = NumberListParser.Parse(BulkInputText);
        if (numbers.Count == 0)
        {
            return;
        }

        if (numbers.Count > MaxBulkNumbers)
        {
            DialogHelper.ShowError(new DomainValidationException(
                $"Слишком много номеров: {numbers.Count}. Максимум за один массовый поиск — {MaxBulkNumbers}."));
            return;
        }

        var version = Interlocked.Increment(ref _searchVersion);
        _massSearchCancellation?.Cancel();
        _massSearchCancellation?.Dispose();
        _massSearchCancellation = new CancellationTokenSource();
        var cancellationToken = _massSearchCancellation.Token;

        ShowBulkChoice = false;
        IsSearching = true;
        SearchStatusText = "Массовый поиск…";
        try
        {
            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                if (MassSearchStarted is not null)
                {
                    await MassSearchStarted();
                }

                var result = await _search.SearchManyAsync(numbers, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (version != _searchVersion)
                {
                    return;
                }

                BuildMassSummary(result);
                ShowMassResult = true;
                QuickQuery = string.Empty;
                HasSearched = false;
                Rows = new ObservableCollection<SearchResultRow>();
                RowsView = CreateRowsView(Rows);
                ConfirmationText = string.Empty;
                UpdateResultState();
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
        finally
        {
            if (version == _searchVersion)
            {
                IsSearching = false;
                SearchStatusText = string.Empty;
            }
        }
    }

    [RelayCommand]
    private async Task AddAsOemListAsync()
    {
        var numbers = NumberListParser.Parse(BulkInputText);
        if (numbers.Count == 0 || OpenAddItemWithNumbers is null)
        {
            return;
        }

        ShowBulkChoice = false;
        await OpenAddItemWithNumbers(numbers);
    }

    [RelayCommand]
    private void CloseMassResult()
    {
        ShowMassResult = false;
        MassSummaryLines = new ObservableCollection<string>();
        MassNotFoundText = string.Empty;
        UpdateResultState();
    }

    [RelayCommand]
    private void CopyMassResult()
    {
        var builder = new StringBuilder();
        foreach (var line in MassSummaryLines)
        {
            builder.AppendLine(line);
        }

        if (MassNotFoundText.Length > 0)
        {
            builder.AppendLine(MassNotFoundText);
        }

        if (builder.Length == 0)
        {
            return;
        }

        try
        {
            Clipboard.SetText(builder.ToString());
            AppFeedback.Send("Сводка массового поиска скопирована");
        }
        catch (Exception ex)
        {
            DialogHelper.ShowError(ex);
        }
    }

    [RelayCommand]
    private void TogglePreview()
    {
        PreviewVisible = !PreviewVisible;
        if (_uiSettings is not null)
        {
            _uiSettings.PreviewVisible = PreviewVisible;
        }
    }

    private void BuildMassSummary(MassSearchResult result)
    {
        var lines = new List<string>();
        foreach (var entry in result.Entries)
        {
            var parts = new List<string>();
            if (entry.DirectCount > 0)
            {
                parts.Add($"{entry.DirectCount} точных");
            }

            if (entry.AnalogCount > 0)
            {
                parts.Add($"{entry.AnalogCount} аналогов");
            }

            lines.Add($"{entry.RawNumber}: найдено {string.Join(", ", parts)}");
        }

        MassSummaryLines = new ObservableCollection<string>(lines);
        MassNotFoundText = result.NotFoundNumbers.Count > 0
            ? $"Не найдено: {string.Join(", ", result.NotFoundNumbers)}"
            : string.Empty;
    }

    [RelayCommand]
    private void Copy()
    {
        if (Rows.Count == 0)
        {
            AppFeedback.Send("Нет данных для копирования");
            return;
        }

        var headers = new[]
        {
            "Номер запчасти", "Наименование", "Производитель", "Применимость к автомобилю",
            "Единица измерения", "Текущий остаток", "Статус наличия", "Тип результата", "Найденный или общий номер",
        };
        var rows = Rows
            .Select(row => (IReadOnlyList<object?>)new object?[]
            {
                row.Item.Code, row.Item.Name, row.Item.Manufacturer, row.Item.CarApplicability,
                DisplayTexts.For(row.Item.Unit), row.Stock, row.Status, row.ResultTypeDisplay, row.MatchedNumber,
            })
            .ToList();
        var text = GridCopy.BuildRowsText(headers, rows, withHeaders: true);

        try
        {
            Clipboard.SetText(text);
            AppFeedback.Send($"Скопировано позиций: {Rows.Count}");
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

            SearchErrorText = string.Empty;
            OnPropertyChanged(nameof(IsSearchError));
            IsSearching = true;
            SearchStatusText = "Поиск…";

            await _operationGate.WaitAsync(cancellationToken);
            try
            {
                var result = isQuickMode
                    ? await _search.SearchQuickAsync(quickQuery, cancellationToken)
                    : await _search.SearchAdvancedAsync(criteria!, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                if (version == _searchVersion)
                {
                    ApplyResult(result, isQuickMode, quickQuery);
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
            if (version == _searchVersion)
            {
                SearchErrorText = "Не удалось выполнить поиск. Попробуйте ещё раз.";
                OnPropertyChanged(nameof(IsSearchError));
            }
            DialogHelper.ShowError(ex);
        }
        finally
        {
            if (version == _searchVersion)
            {
                IsSearching = false;
                SearchStatusText = string.Empty;
            }
        }
    }

    private void ApplyResult(SearchResult result, bool isQuickMode, string quickQuery)
    {
        var selectedItemId = SelectedRow?.Item.Id;
        Rows = new ObservableCollection<SearchResultRow>(result.Rows);
        RowsView = CreateRowsView(Rows);
        CounterText = $"Найдено: {result.Total} ({result.DirectCount} прямых, {result.AnalogCount} аналогов)";
        HasSearched = true;
        SearchErrorText = string.Empty;
        OnPropertyChanged(nameof(IsSearchError));
        ConfirmationText = BuildConfirmationText(result, isQuickMode, quickQuery);
        UpdateResultState();
        SelectedRow = selectedItemId.HasValue
            ? result.Rows.FirstOrDefault(r => r.Item.Id == selectedItemId.Value)
            : null;
        OnPropertyChanged(nameof(HasSelectedRow));
    }

    private static ListCollectionView CreateRowsView(ObservableCollection<SearchResultRow> rows)
    {
        var view = new ListCollectionView(rows);
        view.GroupDescriptions.Add(new PropertyGroupDescription(nameof(SearchResultRow.ResultTypeDisplay)));
        return view;
    }

    private static string BuildConfirmationText(SearchResult result, bool isQuickMode, string quickQuery)
    {
        if (isQuickMode)
        {
            var query = quickQuery.Trim();
            if (query.Length == 0)
            {
                return string.Empty;
            }

            var matchedByNumber = result.Rows.Any(r =>
                r.ResultType == SearchResultType.DirectMatch && r.MatchedNumber.Length > 0);
            return matchedByNumber
                ? $"Найдено по OEM: {query}"
                : $"Найдено по запросу: «{query}»";
        }

        return result.Total > 0 ? "Найдено по условиям расширенного поиска" : string.Empty;
    }

    private void UpdateResultState()
    {
        OnPropertyChanged(nameof(IsEmptyResult));
        OnPropertyChanged(nameof(EmptyStateTitle));
        OnPropertyChanged(nameof(EmptyStateDescription));
    }

    private void RestartDebounce()
    {
        _debounce.Stop();
        _debounce.Start();
    }

    private void UpdateFilterState()
    {
        var filters = new List<string>();
        if (QuickQuery.Trim().Length > 0)
        {
            filters.Add($"запрос «{QuickQuery.Trim()}»");
        }
        if (AdvName.Trim().Length > 0)
        {
            filters.Add($"наименование «{AdvName.Trim()}»");
        }
        if (AdvCode.Trim().Length > 0)
        {
            filters.Add($"код «{AdvCode.Trim()}»");
        }
        if (AdvNumber.Trim().Length > 0)
        {
            filters.Add($"номер «{AdvNumber.Trim()}»");
        }
        if (AdvNumberType != DisplayTexts.NumberTypeFilters[0])
        {
            filters.Add($"тип номера: {AdvNumberType}");
        }
        if (AdvManufacturer.Trim().Length > 0)
        {
            filters.Add($"производитель «{AdvManufacturer.Trim()}»");
        }
        if (AdvApplicability.Trim().Length > 0)
        {
            filters.Add($"применимость «{AdvApplicability.Trim()}»");
        }
        if (AdvAvailability != DisplayTexts.AvailabilityFilters[0])
        {
            filters.Add(AdvAvailability);
        }
        if (AdvDiameter.Trim().Length > 0)
        {
            filters.Add($"диаметр {AdvDiameter.Trim()} мм");
        }
        if (AdvThickness.Trim().Length > 0)
        {
            filters.Add($"толщина {AdvThickness.Trim()} мм");
        }
        if (AdvWidth.Trim().Length > 0)
        {
            filters.Add($"ширина {AdvWidth.Trim()} мм");
        }
        if (AdvTolerance.Trim().Length > 0 && AdvTolerance.Trim() != "0")
        {
            filters.Add($"разброс {AdvTolerance.Trim()}%");
        }

        HasActiveFilters = filters.Count > 0;
        ActiveFilterSummary = filters.Count > 0 ? string.Join("; ", filters) : string.Empty;
        OnPropertyChanged(nameof(CanClearQuick));
        UpdateResultState();
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

    partial void OnQuickQueryChanged(string value)
    {
        UpdateFilterState();
        var looksLikeList = IsQuickMode && NumberListParser.LooksLikeList(value);
        if (looksLikeList)
        {
            BulkInputText = value;
        }

        ShowBulkChoice = looksLikeList;
        RestartDebounce();
    }

    partial void OnIsQuickModeChanged(bool value)
    {
        OnPropertyChanged(nameof(IsAdvancedMode));
        UpdateFilterState();
        RestartDebounce();
    }

    partial void OnIsSearchingChanged(bool value) => UpdateResultState();

    partial void OnSelectedRowChanged(SearchResultRow? value)
    {
        OnPropertyChanged(nameof(HasSelectedRow));
        if (Details is not null)
        {
            if (value is null)
            {
                Details.Clear();
            }
            else
            {
                _ = Details.LoadAsync(value.Item, value.Stock);
            }
        }
    }

    partial void OnShowMassResultChanged(bool value) => UpdateResultState();

    partial void OnPreviewVisibleChanged(bool value) => OnPropertyChanged(nameof(IsPreviewHidden));

    partial void OnAdvNameChanged(string value) { UpdateFilterState(); RestartDebounce(); }
    partial void OnAdvCodeChanged(string value) { UpdateFilterState(); RestartDebounce(); }
    partial void OnAdvNumberChanged(string value) { UpdateFilterState(); RestartDebounce(); }
    partial void OnAdvNumberTypeChanged(string value) { UpdateFilterState(); RestartDebounce(); }
    partial void OnAdvManufacturerChanged(string value) { UpdateFilterState(); RestartDebounce(); }
    partial void OnAdvApplicabilityChanged(string value) { UpdateFilterState(); RestartDebounce(); }
    partial void OnAdvAvailabilityChanged(string value) { UpdateFilterState(); RestartDebounce(); }
    partial void OnAdvDiameterChanged(string value) { UpdateFilterState(); RestartDebounce(); }
    partial void OnAdvThicknessChanged(string value) { UpdateFilterState(); RestartDebounce(); }
    partial void OnAdvWidthChanged(string value) { UpdateFilterState(); RestartDebounce(); }
    partial void OnAdvToleranceChanged(string value) { UpdateFilterState(); RestartDebounce(); }
}
