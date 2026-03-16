using System.Windows.Controls;

namespace MMAAgent.Desktop.Views;

public partial class PromotionsView : UserControl
{
    public PromotionsView()
    {
        InitializeComponent();
        Loaded += async (_, __) =>
        {
            if (DataContext is ViewModels.PromotionsViewModel vm)
                await vm.LoadAsync();
        };
    }
}