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

    private void NumbersGrid_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        => Ui.GridCopy.HandleKeyDown(NumbersGrid, e);

    private void RelatedGrid_OnKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        => Ui.GridCopy.HandleKeyDown(RelatedGrid, e);

    private void NumberColumnsMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement target)
        {
            Ui.DataGridColumns.OpenMenu(NumbersGrid, target);
        }
    }

    private void RelatedColumnsMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement target)
        {
            Ui.DataGridColumns.OpenMenu(RelatedGrid, target);
        }
    }

    private void CopyNumberCell_OnClick(object sender, RoutedEventArgs e) => Ui.GridCopy.CopyCurrentCell(NumbersGrid);

    private void CopyNumberRow_OnClick(object sender, RoutedEventArgs e) => Ui.GridCopy.CopySelectedRow(NumbersGrid);
}
