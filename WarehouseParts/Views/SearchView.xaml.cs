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

    private void SearchField_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            _viewModel.FindCommand.Execute(null);
        }
    }

    private void ResultsGrid_OnMouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid { SelectedItem: SearchResultRow row })
        {
            _viewModel.OpenNumbersCommand.Execute(row);
        }
    }
}
