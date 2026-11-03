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
            DetailsView.FocusFirstAction();
        }
    }

    private void ItemsGrid_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (WarehouseParts.Ui.GridCopy.HandleKeyDown(ItemsGrid, e))
        {
            return;
        }

        if (e.Key == Key.Enter && _viewModel.SelectedRow is not null)
        {
            DetailsView.FocusFirstAction();
            e.Handled = true;
        }
    }

    private void NumbersGrid_OnKeyDown(object sender, KeyEventArgs e)
        => WarehouseParts.Ui.GridCopy.HandleKeyDown(NumbersGrid, e);

    private void ColumnsMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement target)
        {
            WarehouseParts.Ui.DataGridColumns.OpenMenu(ItemsGrid, target);
        }
    }

    private void NumberColumnsMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement target)
        {
            WarehouseParts.Ui.DataGridColumns.OpenMenu(NumbersGrid, target);
        }
    }

    private void CopyItemCell_OnClick(object sender, RoutedEventArgs e) => WarehouseParts.Ui.GridCopy.CopyCurrentCell(ItemsGrid);

    private void CopyItemRow_OnClick(object sender, RoutedEventArgs e) => WarehouseParts.Ui.GridCopy.CopySelectedRow(ItemsGrid);

    private void CopyNumberCell_OnClick(object sender, RoutedEventArgs e) => WarehouseParts.Ui.GridCopy.CopyCurrentCell(NumbersGrid);

    private void CopyNumberRow_OnClick(object sender, RoutedEventArgs e) => WarehouseParts.Ui.GridCopy.CopySelectedRow(NumbersGrid);

    private void CopyCodeCell_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: CatalogItemRow row })
        {
            WarehouseParts.Ui.GridCopy.CopyWithFeedback(row.Item.Code, $"Код «{row.Item.Code}» скопирован");
        }
    }

    private void CopyNumberValue_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: NumberRowViewModel row })
        {
            WarehouseParts.Ui.GridCopy.CopyWithFeedback(row.Number.NumberRaw, $"Номер «{row.Number.NumberRaw}» скопирован");
        }
    }

    private void PreviewPanel_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Control))
        {
            ItemsGrid.Focus();
            e.Handled = true;
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
