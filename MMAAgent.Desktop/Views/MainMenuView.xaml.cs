using System.Windows.Controls;
using MMAAgent.Desktop.ViewModels;

namespace MMAAgent.Desktop.Views
{
    public partial class MainMenuView : UserControl
    {
        public MainMenuView()
        {
            InitializeComponent();
        }

        private void NewGame_Click(object sender, System.Windows.RoutedEventArgs e)
            => (DataContext as MainMenuViewModel)?.NewGame();

        private void LoadLast_Click(object sender, System.Windows.RoutedEventArgs e)
            => (DataContext as MainMenuViewModel)?.LoadLast();
    }
}