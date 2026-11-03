using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WarehouseParts.Data;
using WarehouseParts.Services;
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
    private InventoryViewModel? _inventoryVm;
    private int _currentIndex;
    private bool _navigating;

    public MainWindow()
    {
        InitializeComponent();

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

        MenuList.SelectedIndex = 0;
        Closed += (_, _) =>
        {
            foreach (var context in _contexts)
            {
                context.Dispose();
            }
        };
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
        return AttachContext(new OperationDialog(operations, catalog, type, item) { Owner = this }, context);
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
        var viewModel = new SearchViewModel(search);
        viewModel.OpenOperation = (item, type) =>
        {
            CreateOperationDialog(type, item).ShowDialog();
            return Task.CompletedTask;
        };
        viewModel.OpenItemNumbers = item =>
        {
            CreateItemNumbersWindow(item).ShowDialog();
            return Task.CompletedTask;
        };
        return new SearchView(viewModel);
    }

    private UserControl CreateCatalogView()
    {
        var context = CreateContext();
        var operations = new OperationsService(context);
        var catalog = new CatalogService(context, operations);
        var viewModel = new CatalogViewModel(catalog);
        viewModel.OpenItemDialog = row =>
        {
            var dialogContext = CreateContext();
            var dialogOperations = new OperationsService(dialogContext);
            var dialogCatalog = new CatalogService(dialogContext, dialogOperations);
            var dialog = new ItemDialog(dialogCatalog, row) { Owner = this };
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
            return Task.FromResult(AttachContext(dialog, dialogContext).ShowDialog() == true);
        };
        viewModel.OpenBulkDialog = () =>
        {
            var row = viewModel.SelectedRow;
            if (row is not null)
            {
                var dialogContext = CreateContext();
                var dialogOperations = new OperationsService(dialogContext);
                var dialogCatalog = new CatalogService(dialogContext, dialogOperations);
                AttachContext(new BulkNumbersDialog(dialogCatalog, row.Item.Id) { Owner = this }, dialogContext).ShowDialog();
            }

            return Task.CompletedTask;
        };
        viewModel.OpenItemNumbers = item =>
        {
            CreateItemNumbersWindow(item).ShowDialog();
            return Task.CompletedTask;
        };
        return new CatalogView(viewModel);
    }

    private UserControl CreateJournalView()
    {
        var context = CreateContext();
        var operations = new OperationsService(context);
        var viewModel = new JournalViewModel(operations);
        viewModel.OpenOperationDialog = type =>
        {
            CreateOperationDialog(type, null).ShowDialog();
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
        return new SettingsView(new SettingsViewModel(backup));
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
        if (e.Key == Key.N
            && Keyboard.Modifiers == ModifierKeys.Control
            && _currentIndex == CatalogIndex
            && _views[CatalogIndex]?.DataContext is CatalogViewModel vm
            && vm.AddItemCommand.CanExecute(null))
        {
            vm.AddItemCommand.Execute(null);
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
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
}
