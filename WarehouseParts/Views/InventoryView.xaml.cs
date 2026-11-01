using System.Windows.Controls;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Views;

public partial class InventoryView : UserControl
{
    public InventoryView(InventoryViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        Loaded += async (_, _) => await viewModel.InitializeAsync();
    }
}
