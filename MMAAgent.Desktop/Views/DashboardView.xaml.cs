using System.Windows;
using System.Windows.Controls;
using MMAAgent.Desktop.ViewModels;

namespace MMAAgent.Desktop.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();
        }

        private async void AdvanceWeek_Click(object sender, RoutedEventArgs e)
        {
            if (DataContext is GameViewModel vm)
                await vm.AdvanceWeekAsync();
        }
    }
}