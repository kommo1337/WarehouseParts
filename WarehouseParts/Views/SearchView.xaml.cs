using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using WarehouseParts.Services;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Views;

public partial class SearchView : UserControl
{
    private readonly SearchViewModel _viewModel;

    public SearchView(SearchViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
    }

    private void SearchView_OnLoaded(object sender, RoutedEventArgs e)
    {
        FocusQuickSearch();
    }

    private void SearchView_OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (IsVisible)
        {
            FocusQuickSearch();
        }
    }

    public void FocusQuickSearch()
    {
        QuickSearchBox.Focus();
        QuickSearchBox.SelectAll();
    }

    private void SearchField_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Down)
        {
            FocusResultsGrid();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            _viewModel.FindCommand.Execute(null);
            Dispatcher.BeginInvoke(() =>
            {
                if (_viewModel.Rows.Count == 0)
                {
                    return;
                }

                _viewModel.SelectedRow ??= _viewModel.Rows[0];
                if (_viewModel.PreviewVisible)
                {
                    FocusPreview();
                }
                else
                {
                    FocusResultsGrid();
                }
            });
            e.Handled = true;
        }
    }

    private void ResultsGrid_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (WarehouseParts.Ui.GridCopy.HandleKeyDown(ResultsGrid, e))
        {
            return;
        }

        if (e.Key == Key.Escape)
        {
            FocusQuickSearch();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter && _viewModel.SelectedRow is not null && _viewModel.PreviewVisible)
        {
            FocusPreview();
            e.Handled = true;
        }
    }

    private void PreviewPanel_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || (e.Key == Key.Left && Keyboard.Modifiers == ModifierKeys.Control))
        {
            FocusResultsGrid();
            e.Handled = true;
        }
    }

    private void FocusResultsGrid()
    {
        ResultsGrid.Focus();
        if (ResultsGrid.SelectedItem is null && ResultsGrid.Items.Count > 0)
        {
            ResultsGrid.SelectedIndex = 0;
        }
    }

    private void FocusPreview()
    {
        DetailsView.FocusFirstAction();
    }

    private void ColumnsMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement target)
        {
            WarehouseParts.Ui.DataGridColumns.OpenMenu(ResultsGrid, target);
        }
    }

    private void CopyCell_OnClick(object sender, RoutedEventArgs e) => WarehouseParts.Ui.GridCopy.CopyCurrentCell(ResultsGrid);

    private void CopyRow_OnClick(object sender, RoutedEventArgs e) => WarehouseParts.Ui.GridCopy.CopySelectedRow(ResultsGrid);

    private void ResultsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: SearchResultRow })
        {
            _viewModel.OpenNumbersCommand.Execute(null);
        }
    }
}
