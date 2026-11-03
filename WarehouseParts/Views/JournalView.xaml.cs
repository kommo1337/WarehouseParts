using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Views;

public partial class JournalView : UserControl
{
    public JournalView(JournalViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }

    private void JournalGrid_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (WarehouseParts.Ui.GridCopy.HandleKeyDown(JournalGrid, e))
        {
            return;
        }

        if (DataContext is not JournalViewModel viewModel)
        {
            return;
        }

        if (e.Key == Key.PageUp && viewModel.CanPreviousPage)
        {
            viewModel.PreviousPageCommand.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.PageDown && viewModel.CanNextPage)
        {
            viewModel.NextPageCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void ColumnsMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement target)
        {
            WarehouseParts.Ui.DataGridColumns.OpenMenu(JournalGrid, target);
        }
    }

    private void CopyCell_OnClick(object sender, RoutedEventArgs e) => WarehouseParts.Ui.GridCopy.CopyCurrentCell(JournalGrid);

    private void CopyRow_OnClick(object sender, RoutedEventArgs e) => WarehouseParts.Ui.GridCopy.CopySelectedRow(JournalGrid);
}
