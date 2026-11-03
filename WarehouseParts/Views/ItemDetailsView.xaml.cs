using System.Windows.Controls;

namespace WarehouseParts.Views;

public partial class ItemDetailsView : UserControl
{
    public ItemDetailsView()
    {
        InitializeComponent();
    }

    public void FocusFirstAction()
    {
        Dispatcher.BeginInvoke(() => ReceiptButton.Focus());
    }
}
