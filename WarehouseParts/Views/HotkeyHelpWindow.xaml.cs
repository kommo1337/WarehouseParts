using System.Windows;
using System.Windows.Input;

namespace WarehouseParts.Views;

public partial class HotkeyHelpWindow : Window
{
    public HotkeyHelpWindow()
    {
        InitializeComponent();
    }

    private void HotkeyHelpWindow_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape || e.Key == Key.Enter)
        {
            DialogResult = true;
            e.Handled = true;
        }
    }
}
