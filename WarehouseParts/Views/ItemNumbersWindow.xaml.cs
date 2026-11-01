using System.Windows;
using WarehouseParts.Data;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Views;

public partial class ItemNumbersWindow : Window
{
    public ItemNumbersWindow(Item item, CatalogService catalog, SearchService search)
    {
        InitializeComponent();
        var viewModel = new ItemNumbersViewModel(item, catalog, search);
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.LoadAsync();
    }
}
