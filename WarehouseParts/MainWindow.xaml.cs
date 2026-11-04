using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.Ui;
using WarehouseParts.ViewModels;
using WarehouseParts.Views;

namespace WarehouseParts;

public partial class MainWindow : Window
{
    private const int CatalogIndex = 1;
    private const int InventoryIndex = 3;

    private readonly List<AppDbContext> _contexts = new();
    private readonly UserControl?[] _views = new UserControl?[5];
    private readonly Func<UserControl>[] _viewFactories;
    private readonly Func<Task>[] _refreshers;
    private readonly FeedbackSink _feedbackSink;
    private readonly DispatcherTimer _feedbackTimer;
    private readonly UiSettingsService _uiSettings = new(App.DataDirectory);
    private InventoryViewModel? _inventoryVm;
    private int _currentIndex;
    private bool _navigating;

    public MainWindow()
    {
        InitializeComponent();

        _feedbackTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _feedbackTimer.Tick += (_, _) =>
        {
            _feedbackTimer.Stop();
            FeedbackPanel.Visibility = Visibility.Collapsed;
        };
        _feedbackSink = new FeedbackSink(ShowFeedback, Dispatcher);
        Ui.DataGridColumns.Settings = _uiSettings;

        _viewFactories = new Func<UserControl>[]
        {
            CreateSearchView,
            CreateCatalogView,
            CreateJournalView,
            CreateInventoryView,
            CreateSettingsView
        };
        _refreshers = new Func<Task>[]
        {
            async () =>
            {
                if (_views[0]?.DataContext is SearchViewModel vm)
                {
                    await vm.RefreshOnShowAsync();
                }
            },
            async () =>
            {
                if (_views[1]?.DataContext is CatalogViewModel vm)
                {
                    await vm.RefreshAsync();
                }
            },
            async () =>
            {
                if (_views[2]?.DataContext is JournalViewModel vm)
                {
                    await vm.RefreshAsync();
                }
            },
            async () =>
            {
                if (_inventoryVm is not null)
                {
                    await _inventoryVm.ReloadAsync();
                }
            },
            () => Task.CompletedTask
        };

        MenuList.ItemsSource = new List<NavigationEntry>
        {
            new("Поиск", "Найдите запчасть, проверьте остаток, аналоги и проведите операцию.", "F1"),
            new("Каталог", "Ведите справочник позиций, внутренние номера, аналоги и характеристики.", "F2"),
            new("Журнал", "Просматривайте и фильтруйте историю приходов и расходов.", "F3"),
            new("Инвентаризация", "Сравните учётный и фактический остатки и проведите корректировку.", "F4"),
            new("Настройки", "Управляйте резервными копиями и проверяйте расположение данных.", "F5")
        };
        MenuList.SelectedIndex = 0;
        Closed += (_, _) =>
        {
            _feedbackSink.Dispose();
            foreach (var context in _contexts)
            {
                context.Dispose();
            }
        };
    }

    private void ShowFeedback(string message)
    {
        FeedbackText.Text = message;
        FeedbackPanel.Visibility = Visibility.Visible;
        _feedbackTimer.Stop();
        _feedbackTimer.Start();
    }

    private AppDbContext CreateContext()
    {
        var context = new AppDbContext(App.DbOptions);
        _contexts.Add(context);
        return context;
    }

    private TWindow AttachContext<TWindow>(TWindow window, AppDbContext context) where TWindow : Window
    {
        window.Closed += (_, _) =>
        {
            context.Dispose();
            _contexts.Remove(context);
        };
        return window;
    }

    private OperationDialog CreateOperationDialog(OperationType? type, Item? item)
    {
        var context = CreateContext();
        var operations = new OperationsService(context);
        var catalog = new CatalogService(context, operations);
        var dialog = new OperationDialog(operations, catalog, type, item) { Owner = this };
        dialog.Closed += async (_, _) =>
        {
            await dialog.CancelPendingAsync();
            context.Dispose();
            _contexts.Remove(context);
        };
        return dialog;
    }

    private void ShowOperationDialog(OperationType? type, Item? item)
    {
        var dialog = CreateOperationDialog(type, item);
        if (ShowDialogRestoringFocus(dialog) == true)
        {
            AppFeedback.Send(dialog.SavedMessage ?? "Операция сохранена");
        }
    }

    /// <summary>Показывает модальный диалог и возвращает фокус в элемент, который был активен до его открытия.</summary>
    private bool? ShowDialogRestoringFocus(Window dialog)
    {
        var focused = FocusManager.GetFocusedElement(this);
        var result = dialog.ShowDialog();
        if (focused is UIElement element && element.IsVisible)
        {
            Dispatcher.BeginInvoke(new Action(() => element.Focus()));
        }

        return result;
    }

    private ItemNumbersWindow CreateItemNumbersWindow(Item item)
    {
        var context = CreateContext();
        var operations = new OperationsService(context);
        var catalog = new CatalogService(context, operations);
        var search = new SearchService(context, operations);
        return AttachContext(new ItemNumbersWindow(item, catalog, search) { Owner = this }, context);
    }

    private UserControl CreateSearchView()
    {
        var context = CreateContext();
        var operations = new OperationsService(context);
        var search = new SearchService(context, operations);
        var catalog = new CatalogService(context, operations);
        var details = new ItemDetailsViewModel(catalog, search, operations);
        var viewModel = new SearchViewModel(search, details, _uiSettings);
        viewModel.OpenOperation = (item, type) =>
        {
            ShowOperationDialog(type, item);
            return Task.CompletedTask;
        };
        viewModel.OpenItemNumbers = item =>
        {
            ShowDialogRestoringFocus(CreateItemNumbersWindow(item));
            return Task.CompletedTask;
        };
        viewModel.OpenCatalog = () =>
        {
            MenuList.SelectedIndex = CatalogIndex;
            return Task.CompletedTask;
        };
        viewModel.OpenAddItemWithNumbers = numbers =>
        {
            OpenNewItemDialog(numbers);
            return Task.CompletedTask;
        };
        viewModel.OpenEditItem = item => OpenItemEditDialog(viewModel, catalog, item);

        details.OpenOperation = (item, type) =>
        {
            ShowOperationDialog(type, item);
            return viewModel.RefreshOnShowAsync();
        };
        details.OpenEdit = item => OpenItemEditDialog(viewModel, catalog, item);
        details.AddNumber = item => OpenItemAddNumberDialog(viewModel, catalog, item);
        return new SearchView(viewModel);
    }

    private async Task OpenItemEditDialog(SearchViewModel searchVm, CatalogService catalog, Item item)
    {
        var stock = await catalog.GetRowsAsync();
        var row = stock.FirstOrDefault(r => r.Item.Id == item.Id) ?? new CatalogItemRow { Item = item };
        var dialogContext = CreateContext();
        var dialogOperations = new OperationsService(dialogContext);
        var dialogCatalog = new CatalogService(dialogContext, dialogOperations);
        var dialog = new ItemDialog(dialogCatalog, dialogOperations, row) { Owner = this };
        var saved = ShowDialogRestoringFocus(AttachContext(dialog, dialogContext)) == true;
        if (saved)
        {
            AppFeedback.Send("Позиция сохранена");
            await searchVm.RefreshOnShowAsync();
        }
    }

    private async Task OpenItemAddNumberDialog(SearchViewModel searchVm, CatalogService catalog, Item item)
    {
        var dialogContext = CreateContext();
        var dialogOperations = new OperationsService(dialogContext);
        var dialogCatalog = new CatalogService(dialogContext, dialogOperations);
        var dialog = new NumberDialog(dialogCatalog, item.Id, null) { Owner = this };
        var saved = ShowDialogRestoringFocus(AttachContext(dialog, dialogContext)) == true;
        if (saved)
        {
            await searchVm.RefreshOnShowAsync();
        }
    }

    private UserControl CreateCatalogView()
    {
        var context = CreateContext();
        var operations = new OperationsService(context);
        var catalog = new CatalogService(context, operations);
        var search = new SearchService(context, operations);
        var details = new ItemDetailsViewModel(catalog, search, operations);
        var viewModel = new CatalogViewModel(catalog, details);
        viewModel.ConfirmDelete = message => DialogHelper.Confirm(message);
        viewModel.OpenItemDialog = row =>
        {
            var dialogContext = CreateContext();
            var dialogOperations = new OperationsService(dialogContext);
            var dialogCatalog = new CatalogService(dialogContext, dialogOperations);
            var dialog = new ItemDialog(dialogCatalog, dialogOperations, row) { Owner = this };
            var saved = AttachContext(dialog, dialogContext).ShowDialog() == true;
            return Task.FromResult(new ItemDialogResult(saved, dialog.SavedItemId));
        };
        viewModel.OpenNumberDialog = number =>
        {
            var row = viewModel.SelectedRow;
            if (row is null)
            {
                return Task.FromResult(false);
            }

            var dialogContext = CreateContext();
            var dialogOperations = new OperationsService(dialogContext);
            var dialogCatalog = new CatalogService(dialogContext, dialogOperations);
            var dialog = new NumberDialog(dialogCatalog, row.Item.Id, number) { Owner = this };
            return Task.FromResult(ShowDialogRestoringFocus(AttachContext(dialog, dialogContext)) == true);
        };
        viewModel.OpenBulkDialog = () =>
        {
            var row = viewModel.SelectedRow;
            if (row is not null)
            {
                var dialogContext = CreateContext();
                var dialogOperations = new OperationsService(dialogContext);
                var dialogCatalog = new CatalogService(dialogContext, dialogOperations);
                var bulkDialog = AttachContext(new BulkNumbersDialog(dialogCatalog, row.Item.Id) { Owner = this }, dialogContext);
                ShowDialogRestoringFocus(bulkDialog);
            }

            return Task.CompletedTask;
        };
        viewModel.OpenItemNumbers = item =>
        {
            ShowDialogRestoringFocus(CreateItemNumbersWindow(item));
            return Task.CompletedTask;
        };
        details.OpenOperation = (item, type) =>
        {
            ShowOperationDialog(type, item);
            return viewModel.RefreshAsync();
        };
        details.OpenEdit = _ =>
        {
            viewModel.EditItemCommand.Execute(null);
            return Task.CompletedTask;
        };
        details.AddNumber = _ =>
        {
            viewModel.AddNumberCommand.Execute(null);
            return Task.CompletedTask;
        };
        return new CatalogView(viewModel);
    }

    private UserControl CreateJournalView()
    {
        var context = CreateContext();
        var operations = new OperationsService(context);
        var viewModel = new JournalViewModel(operations);
        viewModel.ConfirmDelete = message => DialogHelper.Confirm(message);
        viewModel.OpenOperationDialog = type =>
        {
            ShowOperationDialog(type, null);
            return Task.CompletedTask;
        };
        return new JournalView(viewModel);
    }

    private UserControl CreateInventoryView()
    {
        var context = CreateContext();
        var operations = new OperationsService(context);
        _inventoryVm = new InventoryViewModel(new InventoryService(context, operations));
        return new InventoryView(_inventoryVm);
    }

    private UserControl CreateSettingsView()
    {
        var context = CreateContext();
        var backup = new BackupService(context, Path.Combine(App.DataDirectory, "sklad.db"), App.BackupDirectory);
        return new SettingsView(new SettingsViewModel(backup, _uiSettings));
    }

    private void MenuList_OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_navigating)
        {
            return;
        }

        var index = MenuList.SelectedIndex;
        if (index < 0 || index >= _views.Length)
        {
            return;
        }

        if (_currentIndex == InventoryIndex && index != InventoryIndex && _inventoryVm?.HasUnapplied == true
            && !DialogHelper.Confirm("Есть непримененные расхождения. Уйти без применения?"))
        {
            _navigating = true;
            MenuList.SelectedIndex = _currentIndex;
            _navigating = false;
            return;
        }

        if (_currentIndex == InventoryIndex && index != InventoryIndex)
        {
            _inventoryVm?.Reset();
        }

        var view = _views[index];
        if (view is null)
        {
            view = _viewFactories[index]();
            _views[index] = view;
        }

        if (index != _currentIndex)
        {
            CancelPending(_views[_currentIndex]);
        }

        ContentHost.Content = view;
        _currentIndex = index;
        _ = _refreshers[index]();
    }

    private static void CancelPending(UserControl? view)
    {
        switch (view?.DataContext)
        {
            case SearchViewModel search:
                search.CancelPending();
                break;
            case JournalViewModel journal:
                journal.CancelPending();
                break;
            case InventoryViewModel inventory:
                inventory.CancelPending();
                break;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        var modifiers = Keyboard.Modifiers;

        if (modifiers == ModifierKeys.None)
        {
            var sectionIndex = e.Key switch
            {
                Key.F1 => 0,
                Key.F2 => 1,
                Key.F3 => 2,
                Key.F4 => 3,
                Key.F5 => 4,
                _ => -1
            };
            if (sectionIndex >= 0)
            {
                NavigateToSection(sectionIndex);
                e.Handled = true;
                return;
            }
        }

        if (modifiers == ModifierKeys.Control)
        {
            switch (e.Key)
            {
                case Key.N:
                    OpenNewItemDialog();
                    e.Handled = true;
                    return;
                case Key.F:
                    NavigateToSection(0);
                    FocusSearchQuery();
                    e.Handled = true;
                    return;
                case Key.OemQuestion:
                    ShowHotkeyHelp();
                    e.Handled = true;
                    return;
                case Key.R:
                    OperateSelected(OperationType.Receipt);
                    e.Handled = true;
                    return;
                case Key.I:
                    OperateSelected(OperationType.Issue);
                    e.Handled = true;
                    return;
            }
        }

        base.OnKeyDown(e);
    }

    /// <summary>Ctrl+R/Ctrl+I: приход или расход по позиции, выбранной в текущем разделе.</summary>
    private void OperateSelected(OperationType type)
    {
        Item? item = _currentIndex switch
        {
            0 when _views[0]?.DataContext is SearchViewModel search
                => search.SelectedRow?.Item ?? search.Details?.Item,
            1 when _views[1]?.DataContext is CatalogViewModel catalog
                => catalog.SelectedRow?.Item ?? catalog.Details?.Item,
            _ => null,
        };

        if (item is null)
        {
            AppFeedback.Send("Сначала выберите позицию в таблице или карточке");
            return;
        }

        ShowOperationDialog(type, item);
        _ = _refreshers[_currentIndex]();
    }

    private void NavigateToSection(int index)
    {
        if (MenuList.SelectedIndex == index)
        {
            return;
        }

        MenuList.SelectedIndex = index;
    }

    private void FocusSearchQuery()
    {
        if (_views[0] is SearchView searchView)
        {
            searchView.FocusQuickSearch();
        }
    }

    private void OpenNewItemDialog(IReadOnlyList<string>? initialNumbers = null)
    {
        var dialogContext = CreateContext();
        var dialogOperations = new OperationsService(dialogContext);
        var dialogCatalog = new CatalogService(dialogContext, dialogOperations);
        var dialog = new ItemDialog(dialogCatalog, dialogOperations, null, initialNumbers) { Owner = this };
        var saved = ShowDialogRestoringFocus(AttachContext(dialog, dialogContext)) == true;
        if (!saved)
        {
            return;
        }

        AppFeedback.Send("Позиция сохранена");

        if (_views[CatalogIndex]?.DataContext is CatalogViewModel catalogVm)
        {
            _ = catalogVm.RefreshAsync();
        }

        if (_currentIndex != CatalogIndex)
        {
            _ = _refreshers[_currentIndex]();
        }
    }

    private void ShowHotkeyHelp()
    {
        new HotkeyHelpWindow { Owner = this }.ShowDialog();
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_inventoryVm?.HasUnapplied == true
            && !DialogHelper.Confirm("Есть непримененные расхождения. Уйти без применения?"))
        {
            e.Cancel = true;
            return;
        }

        CancelPending(_views[_currentIndex]);
        base.OnClosing(e);
    }

    private sealed record NavigationEntry(string Name, string Description, string Hotkey)
    {
        public override string ToString() => Name;
    }
}
