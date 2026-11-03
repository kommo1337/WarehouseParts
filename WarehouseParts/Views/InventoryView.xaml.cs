using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
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

    private void FactsGrid_OnKeyDown(object sender, KeyEventArgs e)
        => WarehouseParts.Ui.GridCopy.HandleKeyDown(FactsGrid, e);

    private void ColumnsMenu_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is UIElement target)
        {
            WarehouseParts.Ui.DataGridColumns.OpenMenu(FactsGrid, target);
        }
    }

    private void FactInput_OnKeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox current || e.Key != Key.Enter)
        {
            return;
        }

        BindingOperations.GetBindingExpression(current, TextBox.TextProperty)?.UpdateSource();
        var boxes = GetFactInputBoxes(FactsGrid).ToList();
        var index = boxes.IndexOf(current);
        if (index >= 0 && index < boxes.Count - 1)
        {
            e.Handled = true;
            boxes[index + 1].Focus();
            boxes[index + 1].SelectAll();
        }
    }

    private static IEnumerable<TextBox> GetFactInputBoxes(DependencyObject root)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is TextBox { Name: "FactInputBox" } box)
            {
                yield return box;
            }

            foreach (var nested in GetFactInputBoxes(child))
            {
                yield return nested;
            }
        }
    }
}
