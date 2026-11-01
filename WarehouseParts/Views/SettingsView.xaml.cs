using System.Windows.Controls;
using WarehouseParts.ViewModels;

namespace WarehouseParts.Views;

public partial class SettingsView : UserControl
{
    public SettingsView(SettingsViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
