using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Views;

public partial class CatalogView : UserControl
{
    private readonly CatalogViewModel _viewModel;

    public CatalogView(CatalogViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void ItemsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: CatalogItemRow })
        {
            _viewModel.ShowNumbersCommand.Execute(null);
        }
    }

    private void ItemsGrid_OnPreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var element = e.OriginalSource as DependencyObject;
        while (element is not null and not DataGridRow)
        {
            element = VisualTreeHelper.GetParent(element);
        }

        if (element is DataGridRow row)
        {
            ItemsGrid.SelectedItem = row.Item;
        }
    }
}
