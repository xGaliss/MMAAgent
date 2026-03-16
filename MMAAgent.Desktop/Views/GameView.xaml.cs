using System.Windows.Controls;
using MMAAgent.Desktop.ViewModels;

namespace MMAAgent.Desktop.Views
{
    public partial class GameView : UserControl
    {
        public GameView()
        {
            InitializeComponent();
        }

        private async void AdvanceWeek_Click(object sender, System.Windows.RoutedEventArgs e)
        {
            if (DataContext is GameViewModel vm)
                await vm.AdvanceWeekAsync();
        }
    }
}