using System.Windows.Controls;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Views;

public partial class JournalView : UserControl
{
    public JournalView(JournalViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
